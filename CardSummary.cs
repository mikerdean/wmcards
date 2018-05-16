using System.Collections.Generic;

namespace wmcards
{
    class CardSummary
    {
        public IReadOnlyList<CardItem> Cards { get; }
        public IReadOnlyDictionary<string, string> FactionNames { get; }

        public CardSummary(IReadOnlyList<CardItem> cards, IReadOnlyDictionary<string, string> factionNames)
        {
            Cards = cards;
            FactionNames = factionNames;
        }
    }
}
