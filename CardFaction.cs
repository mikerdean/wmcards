using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace wmcards
{
    public class CardFaction
    {
        public string FactionName { get; private set; }
        public IReadOnlyList<CardItem> Warcasters { get; private set; }
        public IReadOnlyList<CardItem> Warlocks { get; private set; }
        public IReadOnlyList<CardItem> Warbeasts { get; private set; }
        public IReadOnlyList<CardItem> Warjacks { get; private set; }
        public IReadOnlyList<CardItem> Units { get; private set; }
        public IReadOnlyList<CardItem> Solos { get; private set; }

        public string FactionType
        {
            get
            {
                if (Warcasters.Count > 0 && Warjacks.Count > 0)
                {
                    return "Warmachine";
                }
                else if (Warlocks.Count > 0 && Warbeasts.Count > 0)
                {
                    return "Hordes";
                }
                else
                {
                    return "Unknown";
                }
            }
        }

        public bool ShouldSerializeWarcasters()
        {
            return Warcasters.Count > 0;
        }

        public bool ShouldSerializeWarlocks()
        {
            return Warlocks.Count > 0;
        }

        public bool ShouldSerializeWarbeasts()
        {
            return Warbeasts.Count > 0;
        }

        public bool ShouldSerializeWarjacks()
        {
            return Warjacks.Count > 0;
        }

        public CardFaction(string factionName, IEnumerable<CardItem> cards)
        {
            if (string.IsNullOrWhiteSpace(factionName))
            {
                throw new ArgumentNullException(nameof(factionName));
            }

            if (cards == null)
            {
                throw new ArgumentNullException(nameof(cards));
            }

            FactionName = factionName;
            Warcasters = new ReadOnlyCollection<CardItem>(cards.Where(c => c.Job.Equals("warcaster", StringComparison.OrdinalIgnoreCase)).ToList());
            Warlocks = new ReadOnlyCollection<CardItem>(cards.Where(c => c.Job.Equals("warlock", StringComparison.OrdinalIgnoreCase)).ToList());
            Warbeasts = new ReadOnlyCollection<CardItem>(cards.Where(c => c.Job.Equals("warbeasts", StringComparison.OrdinalIgnoreCase)).ToList());
            Warjacks = new ReadOnlyCollection<CardItem>(cards.Where(c => c.Job.Equals("warjack", StringComparison.OrdinalIgnoreCase)).ToList());
            Units = new ReadOnlyCollection<CardItem>(cards.Where(c => c.Job.Equals("unit", StringComparison.OrdinalIgnoreCase)).ToList());
            Solos = new ReadOnlyCollection<CardItem>(cards.Where(c => c.Job.Equals("solo", StringComparison.OrdinalIgnoreCase)).ToList());
        }
    }
}