using Core;
using Core.GAS;
using Core.Command;

namespace Gameplay.Judgment
{
    public struct ChipAllocation
    {
        public int PlayerId;
        public int Rock;
        public int Scissors;
        public int Paper;

        public int Total => Rock + Scissors + Paper;

        public ChipAllocation(int playerId)
        {
            PlayerId = playerId;
            Rock = 0;
            Scissors = 0;
            Paper = 0;
        }

        public void Set(int rock, int scissors, int paper)
        {
            Rock = rock;
            Scissors = scissors;
            Paper = paper;
        }

        public void SetDefault()
        {
            Rock = 1;
            Scissors = 1;
            Paper = 1; // 1-1-1 neutral
        }

        public void SetRandom(DeterministicRandom random)
        {
            int remaining = GameConfig.ChipsPerPlayer;
            Rock = 0;
            Scissors = 0;
            Paper = 0;
            for (int i = 0; i < remaining; i++)
            {
                int choice = random.Next(3);
                if (choice == 0) Rock++;
                else if (choice == 1) Scissors++;
                else Paper++;
            }
        }

        public int GetChips(int hand)
        {
            if (hand == HandType.Rock) return Rock;
            if (hand == HandType.Scissors) return Scissors;
            if (hand == HandType.Paper) return Paper;
            return 0;
        }
    }

    public static class HandType
    {
        public const int Rock = 0;
        public const int Scissors = 1;
        public const int Paper = 2;

        // a beats b? Rock(0) beats Scissors(1), Scissors(1) beats Paper(2), Paper(2) beats Rock(0)
        public static bool Beats(int a, int b) => (a + 1) % 3 == b;

        // Who does 'hand' beat?
        public static int BeatsWho(int hand) => (hand + 1) % 3;

        // Who beats 'hand'?
        public static int BeatenBy(int hand) => (hand + 2) % 3;
    }
}
