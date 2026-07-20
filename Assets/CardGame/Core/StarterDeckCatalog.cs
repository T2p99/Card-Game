using System;
using System.Collections.Generic;

namespace CardGame.Core
{
    public sealed class DeckEntry
    {
        public string CardId { get; }
        public int Count { get; }

        public DeckEntry(string cardId, int count)
        {
            CardId = cardId;
            Count = count;
        }
    }

    public sealed class DeckRecipe
    {
        public string Id { get; }
        public string Name { get; }
        public IReadOnlyList<DeckEntry> Entries { get; }

        public DeckRecipe(string id, string name, params DeckEntry[] entries)
        {
            Id = id;
            Name = name;
            Entries = Array.AsReadOnly(entries);
        }

        public List<CardDefinition> Build(IReadOnlyDictionary<string, CardDefinition> catalog)
        {
            var deck = new List<CardDefinition>();
            foreach (var entry in Entries)
            {
                if (!catalog.TryGetValue(entry.CardId, out var card))
                    throw new InvalidOperationException($"Deck {Name} references unknown card {entry.CardId}.");

                for (var i = 0; i < entry.Count; i++) deck.Add(card);
            }

            return deck;
        }
    }

    public static class StarterDeckCatalog
    {
        public const string CardDatabaseSource = "https://db.duelmasters.us/search?card_set=tcg_only";
        public const string ShobuDeckSource = "https://duelmasters.fandom.com/wiki/Shobu%27s_Fire_Deck";
        public const string KokujoDeckSource = "https://duelmasters.fandom.com/wiki/Kokujo%27s_Darkness_Deck_%28TCG%29";

        public static readonly IReadOnlyDictionary<string, CardDefinition> Cards = BuildCatalog();

        public static readonly DeckRecipe ShobusFireDeck = new DeckRecipe(
            "tcg-shobu-fire", "Shobu's Fire Deck",
            E("dm02-034", 2), E("dm01-067", 3), E("dm01-070", 3), E("dm01-071", 1),
            E("dm01-073", 2), E("dm01-074", 4), E("dm02-039", 1), E("dm01-075", 1),
            E("dm01-077", 4), E("dm02-041", 2), E("dm01-080", 3), E("dm01-081", 1),
            E("dm02-042", 1), E("dm01-082", 2), E("dm02-043", 4), E("dm01-085", 1),
            E("dm01-086", 1), E("dm01-087", 2), E("dm01-088", 2));

        public static readonly DeckRecipe KokujosDarknessDeck = new DeckRecipe(
            "tcg-kokujo-darkness", "Kokujo's Darkness Deck",
            E("dm02-023", 1), E("dm01-046", 3), E("dm01-047", 2), E("dm01-048", 2),
            E("dm02-024", 2), E("dm01-051", 1), E("dm01-052", 1), E("dm01-053", 3),
            E("dm02-027", 1), E("dm01-054", 3), E("dm01-056", 1), E("dm02-029", 1),
            E("dm02-030", 4), E("dm02-032", 2), E("dm01-058", 1), E("dm02-033", 1),
            E("dm01-060", 2), E("dm01-061", 1), E("dm01-062", 3), E("dm01-063", 1),
            E("dm01-065", 1), E("dm01-066", 3));

        private static IReadOnlyDictionary<string, CardDefinition> BuildCatalog()
        {
            var cards = new Dictionary<string, CardDefinition>(StringComparer.OrdinalIgnoreCase);

            Add(cards, C("dm02-034", "Armored Cannon Balbaro", Civilization.Fire, 3, CardType.EvolutionCreature, "Human", 3000, "DM-02", "34/55"));
            Add(cards, C("dm01-067", "Armored Walker Urherion", Civilization.Fire, 4, CardType.Creature, "Armorloid", 3000, "DM-01", "67/110"));
            Add(cards, C("dm01-070", "Brawler Zyler", Civilization.Fire, 2, CardType.Creature, "Human", 1000, "DM-01", "70/110"));
            Add(cards, C("dm01-071", "Burning Power", Civilization.Fire, 1, CardType.Spell, null, 0, "DM-01", "71/110"));
            Add(cards, C("dm01-073", "Crimson Hammer", Civilization.Fire, 2, CardType.Spell, null, 0, "DM-01", "73/110"));
            Add(cards, C("dm01-074", "Deadly Fighter Braid Claw", Civilization.Fire, 1, CardType.Creature, "Dragonoid", 1000, "DM-01", "74/110"));
            Add(cards, C("dm02-039", "Dogarn, the Marauder", Civilization.Fire, 3, CardType.Creature, "Armorloid", 2000, "DM-02", "39/55"));
            Add(cards, C("dm01-075", "Draglide", Civilization.Fire, 5, CardType.Creature, "Dragonoid", 5000, "DM-01", "75/110"));
            Add(cards, C("dm01-077", "Fatal Attacker Horvath", Civilization.Fire, 3, CardType.Creature, "Human", 2000, "DM-01", "77/110"));
            Add(cards, C("dm02-041", "Galsaur", Civilization.Fire, 5, CardType.Creature, "Rock Beast", 4000, "DM-02", "41/55"));
            Add(cards, C("dm01-080", "Immortal Baron, Vorg", Civilization.Fire, 2, CardType.Creature, "Human", 2000, "DM-01", "80/110"));
            Add(cards, C("dm01-081", "Magma Gazer", Civilization.Fire, 3, CardType.Spell, null, 0, "DM-01", "81/110"));
            Add(cards, C("dm02-042", "Metalwing Skyterror", Civilization.Fire, 7, CardType.Creature, "Armored Wyvern", 6000, "DM-02", "42/55"));
            Add(cards, C("dm01-082", "Meteosaur", Civilization.Fire, 5, CardType.Creature, "Rock Beast", 2000, "DM-01", "82/110"));
            Add(cards, C("dm02-043", "Mini Titan Gett", Civilization.Fire, 2, CardType.Creature, "Human", 2000, "DM-02", "43/55"));
            Add(cards, C("dm01-085", "Rothus, the Traveler", Civilization.Fire, 4, CardType.Creature, "Armorloid", 4000, "DM-01", "85/110"));
            Add(cards, C("dm01-086", "Stonesaur", Civilization.Fire, 5, CardType.Creature, "Rock Beast", 4000, "DM-01", "86/110"));
            Add(cards, C("dm01-087", "Super Explosive Volcanodon", Civilization.Fire, 4, CardType.Creature, "Dragonoid", 2000, "DM-01", "87/110"));
            Add(cards, C("dm01-088", "Tornado Flame", Civilization.Fire, 5, CardType.Spell, null, 0, "DM-01", "88/110"));

            Add(cards, C("dm02-023", "Amber Piercer", Civilization.Darkness, 4, CardType.Creature, "Brain Jacker", 2000, "DM-02", "23/55"));
            Add(cards, C("dm01-046", "Bloody Squito", Civilization.Darkness, 2, CardType.Creature, "Brain Jacker", 4000, "DM-01", "46/110"));
            Add(cards, C("dm01-047", "Bone Assassin, the Ripper", Civilization.Darkness, 4, CardType.Creature, "Living Dead", 2000, "DM-01", "47/110"));
            Add(cards, C("dm01-048", "Bone Spider", Civilization.Darkness, 3, CardType.Creature, "Living Dead", 5000, "DM-01", "48/110"));
            Add(cards, C("dm02-024", "Chaos Worm", Civilization.Darkness, 5, CardType.EvolutionCreature, "Parasite Worm", 5000, "DM-02", "24/55"));
            Add(cards, C("dm01-051", "Dark Raven, Shadow of Grief", Civilization.Darkness, 4, CardType.Creature, "Ghost", 1000, "DM-01", "51/110"));
            Add(cards, C("dm01-052", "Dark Reversal", Civilization.Darkness, 2, CardType.Spell, null, 0, "DM-01", "52/110"));
            Add(cards, C("dm01-053", "Death Smoke", Civilization.Darkness, 4, CardType.Spell, null, 0, "DM-01", "53/110"));
            Add(cards, C("dm02-027", "General Dark Fiend", Civilization.Darkness, 5, CardType.Creature, "Dark Lord", 6000, "DM-02", "27/55"));
            Add(cards, C("dm01-054", "Ghost Touch", Civilization.Darkness, 2, CardType.Spell, null, 0, "DM-01", "54/110"));
            Add(cards, C("dm01-056", "Gigagiele", Civilization.Darkness, 5, CardType.Creature, "Chimera", 3000, "DM-01", "56/110"));
            Add(cards, C("dm02-029", "Gray Balloon, Shadow of Greed", Civilization.Darkness, 3, CardType.Creature, "Ghost", 3000, "DM-02", "29/55"));
            Add(cards, C("dm02-030", "Horrid Worm", Civilization.Darkness, 3, CardType.Creature, "Parasite Worm", 2000, "DM-02", "30/55"));
            Add(cards, C("dm02-032", "Marrow Ooze, the Twister", Civilization.Darkness, 1, CardType.Creature, "Living Dead", 1000, "DM-02", "32/55"));
            Add(cards, C("dm01-058", "Masked Horror, Shadow of Scorn", Civilization.Darkness, 5, CardType.Creature, "Ghost", 1000, "DM-01", "58/110"));
            Add(cards, C("dm02-033", "Poison Worm", Civilization.Darkness, 4, CardType.Creature, "Parasite Worm", 3000, "DM-02", "33/55"));
            Add(cards, C("dm01-060", "Skeleton Soldier, the Defiled", Civilization.Darkness, 4, CardType.Creature, "Living Dead", 3000, "DM-01", "60/110"));
            Add(cards, C("dm01-061", "Stinger Worm", Civilization.Darkness, 3, CardType.Creature, "Parasite Worm", 5000, "DM-01", "61/110"));
            Add(cards, C("dm01-062", "Swamp Worm", Civilization.Darkness, 7, CardType.Creature, "Parasite Worm", 2000, "DM-01", "62/110"));
            Add(cards, C("dm01-063", "Terror Pit", Civilization.Darkness, 6, CardType.Spell, null, 0, "DM-01", "63/110"));
            Add(cards, C("dm01-065", "Wandering Braineater", Civilization.Darkness, 2, CardType.Creature, "Living Dead", 2000, "DM-01", "65/110"));
            Add(cards, C("dm01-066", "Writhing Bone Ghoul", Civilization.Darkness, 2, CardType.Creature, "Living Dead", 2000, "DM-01", "66/110"));

            Configure(cards, "dm01-070", "Power attacker +2000.", CardImplementationStatus.CoreKeywords,
                new KeywordAbility(CardKeyword.PowerAttacker, 2000));
            Configure(cards, "dm01-074", "Must attack each turn if able.", CardImplementationStatus.CoreKeywords,
                new KeywordAbility(CardKeyword.MustAttackIfAble));
            Configure(cards, "dm01-075", "Must attack each turn if able.", CardImplementationStatus.CoreKeywords,
                new KeywordAbility(CardKeyword.MustAttackIfAble));
            Configure(cards, "dm01-080", "No abilities.", CardImplementationStatus.Complete);

            Configure(cards, "dm02-034", "Evolves from a Human; gains attack power for each other Human.", CardImplementationStatus.DataOnly);
            Configure(cards, "dm01-067", "Gains +2000 power while attacking if you control a Human.", CardImplementationStatus.DataOnly);
            Configure(cards, "dm01-071", "One of your creatures gains power attacker +2000 for the turn.", CardImplementationStatus.DataOnly);
            Configure(cards, "dm01-073", "Destroy an opposing creature with power 2000 or less.", CardImplementationStatus.DataOnly);
            Configure(cards, "dm02-039", "While attacking, gains +2000 power per other tapped creature you control.", CardImplementationStatus.DataOnly);
            Configure(cards, "dm01-077", "Gains +2000 power while attacking if you control an Armorloid.", CardImplementationStatus.DataOnly);
            Configure(cards, "dm02-041", "When it is your only creature, gains power attacker +4000 and double breaker.", CardImplementationStatus.DataOnly);
            Configure(cards, "dm01-081", "One of your creatures gains power attacker +4000 and double breaker for the turn.", CardImplementationStatus.DataOnly);

            Configure(cards, "dm02-023", "When it attacks, you may return a creature from your graveyard to your hand.", CardImplementationStatus.DataOnly);
            Configure(cards, "dm01-046", "Blocker; cannot attack; destroy it after it battles.", CardImplementationStatus.CoreKeywords,
                new KeywordAbility(CardKeyword.Blocker),
                new KeywordAbility(CardKeyword.CannotAttack),
                new KeywordAbility(CardKeyword.DestroyAfterBattle));
            Configure(cards, "dm01-047", "Slayer.", CardImplementationStatus.Complete,
                new KeywordAbility(CardKeyword.Slayer));
            Configure(cards, "dm01-048", "Destroy this creature after it battles.", CardImplementationStatus.Complete,
                new KeywordAbility(CardKeyword.DestroyAfterBattle));
            Configure(cards, "dm02-024", "Evolves from a Parasite Worm; may destroy an opposing creature when summoned.", CardImplementationStatus.DataOnly);
            Configure(cards, "dm01-051", "Blocker.", CardImplementationStatus.Complete,
                new KeywordAbility(CardKeyword.Blocker));
            Configure(cards, "dm01-052", "Shield trigger; return a creature from your graveyard to your hand.", CardImplementationStatus.DataOnly,
                new KeywordAbility(CardKeyword.ShieldTrigger));
            Configure(cards, "dm01-053", "Destroy an opposing untapped creature.", CardImplementationStatus.DataOnly);
            Configure(cards, "dm02-027", "When it attacks, send one of your shields to your graveyard; double breaker.", CardImplementationStatus.DataOnly);
            Configure(cards, "dm01-054", "Shield trigger; your opponent discards a random card.", CardImplementationStatus.DataOnly,
                new KeywordAbility(CardKeyword.ShieldTrigger));
            Configure(cards, "dm01-056", "Slayer.", CardImplementationStatus.Complete,
                new KeywordAbility(CardKeyword.Slayer));
            Configure(cards, "dm02-029", "Blocker; cannot attack players.", CardImplementationStatus.Complete,
                new KeywordAbility(CardKeyword.Blocker),
                new KeywordAbility(CardKeyword.CannotAttackPlayers));

            return cards;
        }

        private static DeckEntry E(string id, int count) => new DeckEntry(id, count);

        private static CardDefinition C(
            string id, string name, Civilization civilization, int cost, CardType type,
            string race, int power, string setCode, string collectorNumber)
        {
            return new CardDefinition
            {
                Id = id,
                Name = name,
                Civilizations = new[] { civilization },
                Cost = cost,
                Type = type,
                Races = race == null ? Array.Empty<string>() : new[] { race },
                Power = power,
                SetCode = setCode,
                CollectorNumber = collectorNumber,
                DataSource = CardDatabaseSource,
                ImplementationStatus = CardImplementationStatus.DataOnly
            };
        }

        private static void Add(IDictionary<string, CardDefinition> cards, CardDefinition card)
        {
            cards.Add(card.Id, card);
        }

        private static void Configure(
            IDictionary<string, CardDefinition> cards,
            string id,
            string rulesText,
            CardImplementationStatus status,
            params KeywordAbility[] abilities)
        {
            var card = cards[id];
            card.RulesText = rulesText;
            card.ImplementationStatus = status;
            card.Abilities = abilities;
        }
    }
}
