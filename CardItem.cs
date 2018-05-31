using System;
using System.Collections.Generic;
using System.Net;

namespace wmcards
{
    public class CardItem
    {
        internal int Id { get; private set; }
        internal string Faction { get; private set; }
        internal string Job { get; private set; }

        public IReadOnlyList<string> Images { get; private set; }
        public string Title { get; private set; }
        public int? Points { get; private set; }
        public int? Points6 { get; private set; }
        public int? Points10 { get; private set; }
        public int? FieldAllowance { get; private set; }
        public int? Focus { get; private set; }
        public int? Fury { get; private set; }
        public int? SizeMinimum { get; private set; }
        public int? SizeMaximum { get; private set; }

        public CardItem(int id, string faction, string job, string title)
        {
            if (id < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(id));
            }

            if (string.IsNullOrWhiteSpace(faction))
            {
                throw new ArgumentNullException(nameof(faction));
            }

            if (string.IsNullOrWhiteSpace(job))
            {
                throw new ArgumentNullException(nameof(job));
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                throw new ArgumentNullException(nameof(title));
            }

            Id = id;
            Faction = faction;
            Job = job;
            Title = EscapeHtmlEntities(title);
        }

        public void UpdateFieldAllowance(int fieldAllowance)
        {
            if (fieldAllowance < -1)
            {
                throw new ArgumentOutOfRangeException(nameof(fieldAllowance));
            }

            FieldAllowance = fieldAllowance;
        }

        public void UpdateFocus(int focus)
        {
            if (focus < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(focus));
            }

            Focus = focus;
        }

        public void UpdateFury(int fury)
        {
            if (fury < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(fury));
            }

            Fury = fury;
        }

        public void UpdatePoints(int points)
        {
            if (points < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(points));
            }

            Points = points;
        }

        public void UpdatePoints6(int points)
        {
            if (points < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(points));
            }

            Points6 = points;
        }

        public void UpdatePoints10(int points)
        {
            if (points < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(points));
            }

            Points10 = points;
        }

        public void UpdateImageNames(IReadOnlyList<string> imageLocations)
        {
            if (imageLocations.Count < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(imageLocations));
            }

            Images = imageLocations;
        }

        public void UpdateSizeMinimum(int size)
        {
            if (size < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(size));
            }

            SizeMinimum = (size + 1);
        }

        public void UpdateSizeMaximum(int size)
        {
            if (size < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(size));
            }

            SizeMaximum = (size + 1);
        }

        private static string EscapeHtmlEntities(string value)
        {
            value = WebUtility.HtmlDecode(value);
            return value.Replace("&comma;", ","); // this is bad but yeah
        }
    }
}