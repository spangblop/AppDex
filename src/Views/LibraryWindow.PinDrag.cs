using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace GameShelf
{
    public partial class LibraryWindow
    {
        private sealed class PinDragLayout
        {
            internal readonly Button[] Cards;
            internal readonly Rect[] Slots;
            internal readonly int SourceIndex;
            internal int? PreviewSlot;

            internal PinDragLayout(Button[] cards, string id)
            {
                Cards = cards;
                Slots = cards.Select(card => new Rect(LayoutInformation.GetLayoutSlot(card).TopLeft, card.RenderSize)).ToArray();
                SourceIndex = Array.FindIndex(cards, card => ((Game)card.DataContext).Id == id);
            }

            internal double SlotTop(int index)
            {
                if (index < Slots.Length) return Slots[index].Top;
                return Slots.Length == 0 ? 2 : Slots[Slots.Length - 1].Bottom + Cards[Cards.Length - 1].Margin.Bottom;
            }
        }

        private static DropPosition FindPinDestination(PinDragLayout pins, double y)
        {
            int slot = 0;
            for (int index = 0; index < pins.Slots.Length; index++)
            {
                if (index == pins.SourceIndex) continue;
                var card = pins.Slots[index];
                // Use the preview order after the first hover, so the gap stays put under the pointer.
                double top = pins.PreviewSlot.HasValue
                    ? pins.SlotTop(slot < pins.PreviewSlot.Value ? slot : slot + 1) : card.Top;
                if (y < top + card.Height / 2)
                    return new DropPosition { Kind = DropKind.Pin, Slot = slot, Label = "Pin before " + TileName(pins.Cards[index]) };
                slot++;
            }
            return new DropPosition { Kind = DropKind.Pin, Slot = slot, Label = slot == 0 ? "Pin at the top" : "Pin at the end" };
        }

        private void ProjectPins(PinDragLayout pins, int? slot, bool animate)
        {
            int? previousSlot = pins.PreviewSlot;
            if (previousSlot == slot) return;
            pins.PreviewSlot = slot;
            Pinned.MinHeight = slot.HasValue && pins.SourceIndex < 0
                ? pins.SlotTop(pins.Slots.Length) + preferences.Appearance.PinHeight + 7 : 0;
            int remaining = 0;
            for (int index = 0; index < pins.Cards.Length; index++)
            {
                var button = pins.Cards[index];
                if (index == pins.SourceIndex)
                {
                    button.Opacity = slot.HasValue ? 0 : 1;
                    continue;
                }
                int destination = !slot.HasValue ? index : remaining >= slot.Value ? remaining + 1 : remaining;
                int previous = !previousSlot.HasValue ? index : remaining >= previousSlot.Value ? remaining + 1 : remaining;
                remaining++;
                if (previous == destination) continue;
                MoveTile((TranslateTransform)button.RenderTransform, new Vector(0, pins.SlotTop(destination) - pins.Slots[index].Top),
                    animate ? 135 : 0);
            }
        }

        private Rect PinGuide(PinDragLayout pins, int slot, Rect viewport)
        {
            double y = Pinned.TranslatePoint(new Point(0, pins.SlotTop(slot) + 2), Page).Y;
            y = Math.Max(viewport.Top + 2, Math.Min(y, viewport.Bottom - 4));
            return new Rect(viewport.Left + 3, y, Math.Max(0, viewport.Width - 6), 2);
        }

        private void PinFromDrag(string id, int slot)
        {
            int previous = preferences.Pins.IndexOf(id);
            if (previous == slot) return;
            var app = Games.FirstOrDefault(game => game.Id == id);
            if (app == null) return;
            if (previous >= 0)
            {
                preferences.Pins.RemoveAt(previous);
                var button = Pinned.Children[previous];
                Pinned.Children.RemoveAt(previous);
                Pinned.Children.Insert(slot, button);
            }
            preferences.Pins.Insert(slot, id);
            if (previous < 0) RenderPins(Games.ToDictionary(game => game.Id));
            QueuePreferences();
            Announce(previous < 0 ? "Pinned " + app.Name + "." : "Moved " + app.Name + " in Pinned.");
            PlayUiSound(UiCue.Drop);
        }
    }
}
