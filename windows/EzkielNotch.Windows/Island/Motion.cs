using System.Windows;
using System.Windows.Media.Animation;

namespace EzkielNotch.Island;

/// <summary>A damped spring as a WPF easing: one soft overshoot, then it settles (the Mac's "bouncy"/"smooth").</summary>
public sealed class SpringEase : EasingFunctionBase
{
    /// <summary>How fast the wobble dies out; higher = calmer.</summary>
    public double Decay { get; set; } = 6.5;
    /// <summary>Oscillation speed in radians over the whole animation.</summary>
    public double Frequency { get; set; } = 9;

    protected override double EaseInCore(double t) => 1 - Math.Exp(-Decay * t) * Math.Cos(Frequency * t);

    protected override Freezable CreateInstanceCore() => new SpringEase();

    // EaseIn is the shape we want; WPF's default mode is EaseOut, which would mirror it.
    public SpringEase() => EasingMode = EasingMode.EaseIn;

    /// <summary>Things that arrive: the island opening, a peek.</summary>
    public static SpringEase Bouncy => new() { Decay = 5.6, Frequency = 10 };
    /// <summary>Size changes and closing: barely any overshoot.</summary>
    public static SpringEase Smooth => new() { Decay = 7.5, Frequency = 6 };

    /// <summary>Windows' "Show animations" off → short cross-fades instead of springs.</summary>
    public static bool ReduceMotion => !SystemParameters.ClientAreaAnimation;
}
