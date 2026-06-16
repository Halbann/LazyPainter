namespace LazyPainter;

public class LazyPainterSaveSettings : GameParameters.CustomParameterNode
{
    public static LazyPainterSaveSettings Instance =>
        HighLogic.CurrentGame?.Parameters.CustomParams<LazyPainterSaveSettings>();

    public static bool AllowFlightPainting =>
        Instance?.allowFlightPainting ?? true;

    public override string Section => "Lazy Painter";
    public override string DisplaySection => "Lazy Painter";
    public override int SectionOrder => 1;
    public override string Title => "Painting";
    public override GameParameters.GameMode GameMode => GameParameters.GameMode.ANY;
    public override bool HasPresets => false;

    [GameParameters.CustomParameterUI("Allow painting in flight",
        toolTip = "If disabled, Lazy Painter is only available in the editor.")]
    public bool allowFlightPainting = true;
}
