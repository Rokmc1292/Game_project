using LiarsBatting.Core;

namespace LiarsBatting.Progression
{
    // What I did in one match, counted from my side only. GameBootstrap bumps these as the match
    // plays out (AI and online flows alike) and hands the finished object to ProgressionService.
    public class MatchStats
    {
        public HeroId Hero;
        public bool Online;
        public bool Won;

        public int Guesses;              // full 4-digit guesses I submitted
        public int LiesTold;             // LIE TOKENs I spent on a report
        public int LiesBelieved;         // ...that the opponent trusted
        public int LiesCaught;           // ...that the opponent challenged (caught)
        public int Challenges;           // times I challenged the opponent's report
        public int ChallengesCorrect;    // ...and it really was a lie
        public int ChallengesWrong;      // ...and it was the truth
        public int AbilityUses;          // active hero ability uses
        public int MyDigitsRevealed;     // my digits open at the end
        public int OpponentDigitsRevealed;
    }
}
