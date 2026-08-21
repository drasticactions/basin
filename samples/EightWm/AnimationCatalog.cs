namespace EightWm;

internal static class AnimationCatalog
{
    private const AnimationCurve Decel = AnimationCurve.Deceleration;
    private const AnimationCurve Linear = AnimationCurve.Linear;
    private const AnimationCurve Departure = AnimationCurve.Departure;

    private static readonly AnimationSpec[] Table = Build();

    public static ReadOnlySpan<AnimationSpec> All => Table;

    public static ref readonly AnimationSpec Of(Animation name) => ref Table[(int)name];

    private static AnimationSpec[] Build()
    {
        var table = new AnimationSpec[Enum.GetValues<Animation>().Length];

        Set(table, new AnimationSpec(
            Animation.EnterPage, MotionAxis.X,
            new Track(100, 0, 1000, 0, Decel), Track.None, new Track(0, 1, 170, 0, Linear), 83, 333));

        Set(table, new AnimationSpec(
            Animation.ExitPage, MotionAxis.None,
            Track.None, Track.None, new Track(1, 0, 117, 0, Linear), 0, 0));

        Set(table, new AnimationSpec(
            Animation.EnterContent, MotionAxis.X,
            new Track(40, 0, 550, 0, Decel), Track.None, new Track(0, 1, 170, 0, Linear), 0, 0));

        Set(table, new AnimationSpec(
            Animation.ExitContent, MotionAxis.None,
            Track.None, Track.None, new Track(1, 0, 117, 0, Linear), 0, 0));

        Set(table, new AnimationSpec(
            Animation.ShowEdgeUi, MotionAxis.Y,
            new Track(70, 0, 367, 0, Decel), Track.None, Track.None, 0, 0));

        Set(table, new AnimationSpec(
            Animation.HideEdgeUi, MotionAxis.Y,
            new Track(0, 70, 367, 0, Decel), Track.None, Track.None, 0, 0));

        Set(table, new AnimationSpec(
            Animation.ShowPanel, MotionAxis.X,
            new Track(364, 0, 550, 0, Decel), Track.None, Track.None, 0, 0));

        Set(table, new AnimationSpec(
            Animation.HidePanel, MotionAxis.X,
            new Track(0, 364, 550, 0, Decel), Track.None, Track.None, 0, 0));

        Set(table, new AnimationSpec(
            Animation.FadeIn, MotionAxis.None,
            Track.None, Track.None, new Track(0, 1, 250, 0, Linear), 0, 0));

        Set(table, new AnimationSpec(
            Animation.FadeOut, MotionAxis.None,
            Track.None, Track.None, new Track(1, 0, 167, 0, Linear), 0, 0));

        Set(table, new AnimationSpec(
            Animation.CrossFadeIn, MotionAxis.None,
            Track.None, Track.None, new Track(0, 1, 167, 0, Linear), 0, 0));

        Set(table, new AnimationSpec(
            Animation.CrossFadeOut, MotionAxis.None,
            Track.None, Track.None, new Track(1, 0, 167, 0, Linear), 0, 0));

        Set(table, new AnimationSpec(
            Animation.PointerDown, MotionAxis.None,
            Track.None, new Track(1, 0.975, 167, 0, Decel), Track.None, 0, 0));

        Set(table, new AnimationSpec(
            Animation.PointerUp, MotionAxis.None,
            Track.None, new Track(0.975, 1, 167, 0, Decel), Track.None, 0, 0));

        Set(table, new AnimationSpec(
            Animation.Reposition, MotionAxis.X,
            new Track(1, 0, 367, 0, Decel), Track.None, Track.None, 33, 250));

        Set(table, new AnimationSpec(
            Animation.AddToGrid, MotionAxis.X,
            new Track(1, 0, 400, 0, Decel), new Track(0.85, 1, 120, 0, Decel),
            new Track(0, 1, 120, 0, Linear), 33, 250));

        Set(table, new AnimationSpec(
            Animation.DeleteFromGrid, MotionAxis.X,
            new Track(1, 0, 400, 60, Decel), new Track(1, 0.85, 120, 0, Departure),
            new Track(1, 0, 120, 0, Linear), 33, 250));

        Set(table, new AnimationSpec(
            Animation.Expand, MotionAxis.X,
            new Track(1, 0, 367, 0, Decel), Track.None, new Track(0, 1, 167, 200, Linear), 0, 0));

        Set(table, new AnimationSpec(
            Animation.Collapse, MotionAxis.X,
            new Track(1, 0, 367, 0, Decel), Track.None, new Track(1, 0, 167, 200, Linear), 0, 0));

        Set(table, new AnimationSpec(
            Animation.ShowPopup, MotionAxis.Y,
            new Track(50, 0, 367, 0, Decel), Track.None, new Track(0, 1, 83, 83, Linear), 0, 0));

        Set(table, new AnimationSpec(
            Animation.HidePopup, MotionAxis.None,
            Track.None, Track.None, new Track(1, 0, 83, 0, Linear), 0, 0));

        Set(table, new AnimationSpec(
            Animation.SwipeSelect, MotionAxis.None,
            Track.None, Track.None, new Track(0, 1, 300, 0, Decel), 0, 0));

        Set(table, new AnimationSpec(
            Animation.SwipeDeselect, MotionAxis.None,
            Track.None, Track.None, new Track(1, 0, 300, 0, Decel), 0, 0));

        Set(table, new AnimationSpec(
            Animation.SwipeReveal, MotionAxis.Y,
            new Track(0, 25, 300, 0, Decel), Track.None, Track.None, 0, 0));

        Set(table, new AnimationSpec(
            Animation.DragSourceStart, MotionAxis.None,
            Track.None, new Track(1, 1.05, 240, 0, Decel), new Track(1, 0.65, 240, 0, Linear), 0, 0));

        Set(table, new AnimationSpec(
            Animation.DragSourceEnd, MotionAxis.None,
            Track.None, new Track(1.05, 1, 500, 0, Decel), new Track(0.65, 1, 500, 0, Linear), 0, 0));

        Set(table, new AnimationSpec(
            Animation.DragBetweenEnter, MotionAxis.X,
            new Track(0, 40, 200, 0, Decel), new Track(1, 0.95, 200, 0, Decel), Track.None, 0, 0));

        Set(table, new AnimationSpec(
            Animation.DragBetweenLeave, MotionAxis.X,
            new Track(40, 0, 200, 0, Decel), new Track(0.95, 1, 200, 0, Decel), Track.None, 0, 0));

        Set(table, new AnimationSpec(
            Animation.Peek, MotionAxis.Y,
            new Track(0, 1, 2000, 0, Decel), Track.None, Track.None, 0, 0));

        Set(table, new AnimationSpec(
            Animation.UpdateBadge, MotionAxis.Y,
            new Track(24, 0, 1333, 0, Decel), Track.None, new Track(0, 1, 367, 0, Linear), 0, 0));

        return table;
    }

    private static void Set(AnimationSpec[] table, in AnimationSpec spec) => table[(int)spec.Name] = spec;
}
