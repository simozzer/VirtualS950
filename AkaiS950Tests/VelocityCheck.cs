using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Forms;
using AkaiS950Studio;

/*
 * The velocity strip beside the keyboard.
 *
 * It exists because velocity opens the filter: at the measured 8.34 octaves of full-scale
 * velocity tracking, a strike of 100 sits about 1.2 octaves above the pivot of 65, which
 * on most of the library is the difference between hearing the filter and not. A strip
 * that reads the wrong way round, or that cannot reach the ends, would be worse than the
 * fixed 100 it replaced - so this drives the real control through its own mouse handlers,
 * the way WinForms would, rather than testing a copy of the arithmetic.
 *
 *   VelocityCheck
 */
static class VelocityCheck
{
    static int fails = 0, checks = 0;
    static List<string> problems = new List<string>();

    static void Check(string what, bool ok)
    {
        checks++;
        if (!ok) { fails++; problems.Add(what); }
    }

    [STAThread]
    static void Main()
    {
        var v = new VelocitySlider();
        v.SetBounds(0, 0, 20, 92);          // the height of the keyboard row

        // ---------------------------------------------------------- the defaults
        Check("starts at 100, which is what the keyboard used to play", v.Value == 100);

        // ---------------------------------------------------------- the ends
        v.Value = 0;
        Check("will not go below 1", v.Value == VelocitySlider.MinVelocity);
        v.Value = 999;
        Check("will not go above 127", v.Value == VelocitySlider.MaxVelocity);

        // ------------------------------------------------- top is hard, bottom is soft
        Click(v, 0);
        int atTop = v.Value;
        Click(v, 91);
        int atBottom = v.Value;

        Check("the top of the strip is the hardest strike (" + atTop + ")",
              atTop == VelocitySlider.MaxVelocity);
        Check("the bottom is the softest (" + atBottom + ")",
              atBottom == VelocitySlider.MinVelocity);
        Check("and they are not the wrong way round", atTop > atBottom);

        /*
         * The middle of the groove is a middling strike.
         *
         * Found by feel rather than by arithmetic: the groove does not fill the control -
         * the number sits under it - so the halfway point of the strip is not the halfway
         * point of the range, and a check that assumed otherwise would be testing the
         * layout rather than the mapping.
         */
        int top = -1, bottom = -1;
        for (int y = 0; y < 92; y++)
        {
            Click(v, y);
            if (v.Value == VelocitySlider.MaxVelocity) top = y;
            if (v.Value == VelocitySlider.MinVelocity && bottom < 0) bottom = y;
        }

        Check("the groove has a top and a bottom", top >= 0 && bottom > top);

        if (top >= 0 && bottom > top)
        {
            Click(v, (top + bottom) / 2);
            Check("the middle of the groove is a middling strike (" + v.Value + ")",
                  Math.Abs(v.Value - 64) <= 8);
        }

        // ------------------------------------------------ dragging follows the pointer
        Press(v, 80);
        int low = v.Value;
        Drag(v, 10);
        int high = v.Value;
        Release(v, 10);

        Check("dragging upward raises it (" + low + " -> " + high + ")", high > low);

        Drag(v, 40);
        Check("and a move after the button is up changes nothing", v.Value == high);

        // ------------------------------------------------------------ the wheel
        v.Value = 64;
        Wheel(v, 120);
        Check("the wheel up adds one", v.Value == 65);
        Wheel(v, -120);
        Check("the wheel down takes one off", v.Value == 64);

        // ------------------------------------------------------ it only fires on a change
        int raised = 0;
        v.ValueChanged += (s, e) => raised++;

        v.Value = 64;
        Check("setting it to what it already is raises nothing", raised == 0);
        v.Value = 65;
        Check("a real change raises once", raised == 1);

        // ------------------------------------------- every velocity is reachable by hand
        var seen = new bool[128];
        for (int y = 0; y < 92; y++) { Click(v, y); seen[v.Value] = true; }

        Check("the strip can reach both ends by clicking",
              seen[VelocitySlider.MinVelocity] && seen[VelocitySlider.MaxVelocity]);

        // The strip is 92 pixels tall and velocity has 127 steps, so not every value has a
        // pixel of its own. What matters is that it covers the range without gaps a strike
        // would fall down: the wheel is there for the last few.
        int reachable = 0;
        for (int i = VelocitySlider.MinVelocity; i <= VelocitySlider.MaxVelocity; i++)
            if (seen[i]) reachable++;

        Check("a click reaches most of the range (" + reachable + " of 127)", reachable >= 60);

        Console.WriteLine();
        Console.WriteLine(checks + " checks, " + (fails == 0 ? "ALL PASSED" : fails + " FAILED"));
        foreach (string p in problems) Console.WriteLine("  " + p);
        Environment.Exit(fails == 0 ? 0 : 1);
    }

    static void Click(VelocitySlider v, int y) { Press(v, y); Release(v, y); }

    static void Press(VelocitySlider v, int y)
    {
        Invoke(v, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 10, y, 0));
    }

    static void Drag(VelocitySlider v, int y)
    {
        Invoke(v, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 10, y, 0));
    }

    static void Release(VelocitySlider v, int y)
    {
        Invoke(v, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 10, y, 0));
    }

    static void Wheel(VelocitySlider v, int delta)
    {
        Invoke(v, "OnMouseWheel", new MouseEventArgs(MouseButtons.None, 0, 10, 40, delta));
    }

    /// <summary>The control's own handler, reached the way WinForms would reach it.</summary>
    static void Invoke(object target, string method, object arg)
    {
        MethodInfo m = null;
        for (Type t = target.GetType(); t != null && m == null; t = t.BaseType)
            m = t.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance,
                            null, new[] { arg.GetType() }, null);

        if (m == null) throw new InvalidOperationException("no " + method);
        m.Invoke(target, new[] { arg });
    }
}
