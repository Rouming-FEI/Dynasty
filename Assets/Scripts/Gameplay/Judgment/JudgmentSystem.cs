using Core;
using Core.GAS;
using Core.Command;
using System;

namespace Gameplay.Judgment
{
    /// <summary>
    /// Each player's role outcome from a judgment round.
    /// </summary>
    public enum JudgmentRole : byte
    {
        Neutral,
        Winner,
        Loser
    }

    /// <summary>
    /// Result of a single RPS judgment round. All arrays are sized to playerCount.
    /// Allocated once per turn — not Hot Path.
    /// </summary>
    public struct JudgmentResult
    {
        /// <summary>HandType constant for the winning hand, or -1 on global flat.</summary>
        public int WinnerHand;

        /// <summary>HandType constant for the losing hand (BeatenBy(WinnerHand)).</summary>
        public int LoserHand;

        /// <summary>HandType constant for the neutral hand.</summary>
        public int NeutralHand;

        /// <summary>True when all three hand totals are equal.</summary>
        public bool IsGlobalFlat;

        /// <summary>Per-player JudgmentRole.</summary>
        public JudgmentRole[] Roles;

        /// <summary>Per-player effective chip delta (abs(winnerChips - loserChips)).</summary>
        public int[] EffectiveChips;

        /// <summary>Per-player AP gained this round.</summary>
        public int[] APGained;

        /// <summary>Number of Winner-role players.</summary>
        public int WinnerCount;

        /// <summary>Number of Loser-role players.</summary>
        public int LoserCount;
    }

    /// <summary>
    /// Core RPS judgment algorithm. Determines the winning hand from aggregate chip
    /// allocations, assigns each player a role (Winner/Loser/Neutral), calculates
    /// effective chips, and distributes action points.
    ///
    /// Algorithm per docs/项目介绍.md section 4.1.
    /// </summary>
    public class JudgmentSystem
    {
        /// <summary>
        /// Execute a full judgment round.
        /// </summary>
        /// <param name="allocations">Span of chip allocations, one per player.</param>
        /// <param name="random">Deterministic RNG for tie-breaking in SortByEffectiveChips.</param>
        /// <param name="consecutiveFlatCount">
        /// Running count of consecutive global flats across rounds.
        /// Incremented when IsGlobalFlat, reset otherwise. Passed by ref so the caller
        /// maintains state across rounds.
        /// </param>
        /// <returns>A populated JudgmentResult.</returns>
        public JudgmentResult Resolve(
            Span<ChipAllocation> allocations,
            DeterministicRandom random,
            ref int consecutiveFlatCount)
        {
            int playerCount = allocations.Length;

            JudgmentResult result = new()
            {
                IsGlobalFlat = false,
                Roles = new JudgmentRole[playerCount],
                EffectiveChips = new int[playerCount],
                APGained = new int[playerCount],
            };

            // ——— Step 1: sum total chips for each hand across all players ———
            int totalRock = 0, totalScissors = 0, totalPaper = 0;
            for (int i = 0; i < playerCount; i++)
            {
                totalRock += allocations[i].Rock;
                totalScissors += allocations[i].Scissors;
                totalPaper += allocations[i].Paper;
            }

            // ——— Step 2: determine winner hand ———
            result.WinnerHand = DetermineWinnerHand(totalRock, totalScissors, totalPaper);

            // ——— Step 3: global flat (all three totals equal) ———
            if (result.WinnerHand < 0)
            {
                result.IsGlobalFlat = true;
                consecutiveFlatCount++;

                // Consecutive flat compensation: after 2+ consecutive flats,
                // every player gets 1 AP.
                if (consecutiveFlatCount >= 2)
                {
                    for (int i = 0; i < playerCount; i++)
                    {
                        result.Roles[i] = JudgmentRole.Winner;
                        result.APGained[i] = 1;
                    }
                    consecutiveFlatCount = 0;
                }

                return result;
            }

            // Non-flat round → reset flat counter.
            consecutiveFlatCount = 0;

            // ——— Step 4: LoserHand = the hand beaten by WinnerHand ———
            result.LoserHand = HandType.BeatenBy(result.WinnerHand);

            // ——— Step 5: NeutralHand = the remaining hand (0+1+2 = 3) ———
            result.NeutralHand = 3 - result.WinnerHand - result.LoserHand;

            // ——— Step 6: per-player role and effective chips ———
            result.WinnerCount = 0;
            result.LoserCount = 0;

            for (int i = 0; i < playerCount; i++)
            {
                int winnerChips = allocations[i].GetChips(result.WinnerHand);
                int loserChips = allocations[i].GetChips(result.LoserHand);

                if (winnerChips > loserChips)
                {
                    result.Roles[i] = JudgmentRole.Winner;
                    result.EffectiveChips[i] = winnerChips - loserChips;
                    result.WinnerCount++;
                }
                else if (loserChips > winnerChips)
                {
                    result.Roles[i] = JudgmentRole.Loser;
                    result.EffectiveChips[i] = loserChips - winnerChips;
                    result.LoserCount++;
                }
                else
                {
                    result.Roles[i] = JudgmentRole.Neutral;
                    result.EffectiveChips[i] = 0;
                }
            }

            // ——— Step 7: AP distribution ———
            // Each winner receives AP equal to the number of losers,
            // capped at (playerCount - 1).
            int apPerWinner = Math.Min(result.LoserCount, playerCount - 1);
            for (int i = 0; i < playerCount; i++)
            {
                result.APGained[i] = (result.Roles[i] == JudgmentRole.Winner)
                    ? apPerWinner
                    : 0;
            }

            return result;
        }

        /// <summary>
        /// Determine the winning hand from the three aggregate chip totals.
        /// </summary>
        /// <returns>
        /// HandType.Rock (0), HandType.Scissors (1), HandType.Paper (2),
        /// or -1 when all three totals are equal (global flat).
        /// </returns>
        private static int DetermineWinnerHand(int rock, int scissors, int paper)
        {
            if (rock == scissors && scissors == paper)
                return -1;

            int max = Math.Max(rock, Math.Max(scissors, paper));
            int min = Math.Min(rock, Math.Min(scissors, paper));

            // Count how many hands achieved the max total.
            int atMax = 0;
            if (rock == max) atMax++;
            if (scissors == max) atMax++;
            if (paper == max) atMax++;

            if (atMax == 1)
            {
                // Unique highest total → that hand wins.
                if (rock == max) return HandType.Rock;
                if (scissors == max) return HandType.Scissors;
                return HandType.Paper;
            }
            else
            {
                // Two hands tie for highest + one lowest.
                // The hand that BEATS the lowest hand wins.
                int lowHand;
                if (rock == min) lowHand = HandType.Rock;
                else if (scissors == min) lowHand = HandType.Scissors;
                else lowHand = HandType.Paper;

                return HandType.BeatenBy(lowHand);
            }
        }

        /// <summary>
        /// Sort player IDs in descending order of effective chips.
        /// Ties are broken using the deterministic random generator.
        /// Uses selection sort — player count is capped at 8, so O(n²) is fine.
        /// </summary>
        public static void SortByEffectiveChips(
            int[] playerIds,
            int[] effectiveChips,
            DeterministicRandom random)
        {
            int n = playerIds.Length;
            for (int i = 0; i < n - 1; i++)
            {
                int best = i;
                for (int j = i + 1; j < n; j++)
                {
                    int effJ = effectiveChips[playerIds[j]];
                    int effBest = effectiveChips[playerIds[best]];

                    if (effJ > effBest)
                    {
                        best = j;
                    }
                    else if (effJ == effBest)
                    {
                        // Deterministic random tie-break.
                        if (random.Next(2) == 0)
                            best = j;
                    }
                }

                if (best != i)
                {
                    (playerIds[i], playerIds[best]) = (playerIds[best], playerIds[i]);
                }
            }
        }
    }
}
