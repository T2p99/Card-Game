using System;
using System.Collections.Generic;

namespace CardGame.Core
{
    public enum Civilization
    {
        Light,
        Water,
        Darkness,
        Fire,
        Nature
    }

    public enum CardType
    {
        Creature,
        Spell,
        EvolutionCreature,
        CrossGear,
        EvolutionCrossGear,
        Castle
    }

    public enum CardKeyword
    {
        Blocker,
        ShieldTrigger,
        SpeedAttacker,
        DoubleBreaker,
        TripleBreaker,
        PowerAttacker,
        Slayer,
        AttackUntappedCreature,
        MustAttackIfAble,
        CannotAttack,
        CannotAttackPlayers,
        DestroyAfterBattle
    }

    public enum CardImplementationStatus
    {
        DataOnly,
        CoreKeywords,
        Complete
    }

    [Serializable]
    public sealed class KeywordAbility
    {
        public CardKeyword Keyword;
        public int Value;

        public KeywordAbility(CardKeyword keyword, int value = 0)
        {
            Keyword = keyword;
            Value = value;
        }
    }

    [Serializable]
    public sealed class CardDefinition
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public Civilization[] Civilizations = Array.Empty<Civilization>();
        public int Cost;
        public CardType Type;
        public string[] Races = Array.Empty<string>();
        public int Power;
        public KeywordAbility[] Abilities = Array.Empty<KeywordAbility>();
        public string RulesText = string.Empty;
        public string SetCode = string.Empty;
        public string CollectorNumber = string.Empty;
        public string DataSource = string.Empty;
        public CardImplementationStatus ImplementationStatus = CardImplementationStatus.DataOnly;

        public bool IsCreature => Type == CardType.Creature || Type == CardType.EvolutionCreature;

        public bool HasKeyword(CardKeyword keyword)
        {
            foreach (var ability in Abilities)
            {
                if (ability.Keyword == keyword)
                {
                    return true;
                }
            }

            return false;
        }

        public int KeywordValue(CardKeyword keyword)
        {
            foreach (var ability in Abilities)
            {
                if (ability.Keyword == keyword)
                {
                    return ability.Value;
                }
            }

            return 0;
        }

        public int BreakerCount
        {
            get
            {
                if (HasKeyword(CardKeyword.TripleBreaker)) return 3;
                if (HasKeyword(CardKeyword.DoubleBreaker)) return 2;
                return 1;
            }
        }

        public bool SharesCivilizationWith(CardDefinition other)
        {
            var mine = new HashSet<Civilization>(Civilizations);
            foreach (var civilization in other.Civilizations)
            {
                if (mine.Contains(civilization)) return true;
            }

            return false;
        }
    }
}
