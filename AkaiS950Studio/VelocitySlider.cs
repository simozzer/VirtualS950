using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AkaiS950Studio
{
    /// <summary>
    /// How hard the keyboard strikes, as a narrow strip beside it.
    ///
    /// The keyboard used to play everything at 100, which is not a neutral choice: velocity
    /// opens the filter, and at the measured 8.34 octaves of full-scale velocity tracking a
    /// strike of 100 sits about 1.2 octaves above the pivot of 65. On a programme written
    /// with any velocity-to-filter at all, that is the difference between hearing the
    /// filter and not. SQ BASS on BASS.hfe lands at 1341 Hz struck at 40 and 5328 Hz struck
    /// at 100 - the same programme, two octaves apart.
    ///
    /// Drawn rather than a TrackBar. A TrackBar is forty-odd pixels of chrome before it
    /// draws anything, which is not a thing to put beside a keyboard, and its vertical form
    /// disagrees with itself about which end is the maximum. This one is twenty pixels
    /// wide, reads high at the top the way a fader does, and says its number underneath.
    /// </summary>
    internal sealed class VelocitySlider : Control
    {
        public const int MinVelocity = 1;
        public const int MaxVelocity = 127;

        const int NumberHeight = 14;
        const int TrackInset = 6;

        int _value = 100;
        bool _dragging;

        readonly ToolTip _tip = new ToolTip();

        public event EventHandler ValueChanged;

        public VelocitySlider()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            // It must never take the focus. The arrow keys belong to the lists and the
            // keyboard, and a strip that swallowed them would be a quiet nuisance.
            SetStyle(ControlStyles.Selectable, false);

            Width = 20;
            Cursor = Cursors.Hand;
            UpdateTip();
        }

        /// <summary>The velocity a key press will carry, 1..127.</summary>
        public int Value
        {
            get { return _value; }
            set
            {
                int v = value < MinVelocity ? MinVelocity : (value > MaxVelocity ? MaxVelocity : value);
                if (v == _value) return;

                _value = v;
                UpdateTip();
                Invalidate();

                var h = ValueChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        void UpdateTip()
        {
            _tip.SetToolTip(this,
                "How hard the keyboard strikes: " + _value + " of " + MaxVelocity +
                Environment.NewLine +
                "Velocity opens the filter, so a softer strike is a darker note.");
        }

        Rectangle Track
        {
            get
            {
                int h = Math.Max(1, Height - NumberHeight - TrackInset * 2);
                return new Rectangle(Width / 2 - 3, TrackInset, 6, h);
            }
        }

        /// <summary>Top is loudest, the way a fader reads.</summary>
        int ValueAt(int y)
        {
            var t = Track;
            double f = 1.0 - (y - t.Top) / (double)Math.Max(1, t.Height);
            return (int)Math.Round(MinVelocity + f * (MaxVelocity - MinVelocity));
        }

        int YOf(int velocity)
        {
            var t = Track;
            double f = (velocity - MinVelocity) / (double)(MaxVelocity - MinVelocity);
            return t.Bottom - (int)Math.Round(f * t.Height);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            _dragging = true;
            Value = ValueAt(e.Y);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging) Value = ValueAt(e.Y);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Value = _value + (e.Delta > 0 ? 1 : -1) * (ModifierKeys == Keys.Control ? 10 : 1);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            var t = Track;
            int y = YOf(_value);

            // The groove, then what is dialled in, from the bottom up.
            using (var groove = new SolidBrush(SystemColors.ControlDark))
                g.FillRectangle(groove, t);

            var filled = Rectangle.FromLTRB(t.Left, y, t.Right, t.Bottom);
            if (filled.Height > 0)
                using (var on = new SolidBrush(SystemColors.Highlight))
                    g.FillRectangle(on, filled);

            // The thumb: wider than the groove, so it reads as a fader cap.
            var cap = new Rectangle(t.Left - 4, y - 3, t.Width + 8, 6);
            using (var b = new SolidBrush(SystemColors.ControlLightLight))
                g.FillRectangle(b, cap);
            using (var p = new Pen(SystemColors.ControlDarkDark))
                g.DrawRectangle(p, cap);

            // And the number, small, under the groove.
            using (var f = new Font(Font.FontFamily, 6.5f))
            using (var b = new SolidBrush(SystemColors.GrayText))
            {
                string s = _value.ToString();
                var size = g.MeasureString(s, f);
                g.DrawString(s, f, b, (Width - size.Width) / 2f, Height - NumberHeight);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _tip.Dispose();
            base.Dispose(disposing);
        }
    }
}
