namespace EightWm;

internal static class AnimationCatalog
{
    private const AnimationCurve Decel = AnimationCurve.Deceleration;
    private const AnimationCurve Linear = AnimationCurve.Linear;

    private static readonly AnimationSpec[] Table = Build();

    public static ReadOnlySpan<AnimationSpec> All => Table;

    public static ref readonly AnimationSpec Of(Animation name) => ref Table[(int)name];

    private static AnimationSpec[] Build()
    {
        var table = new AnimationSpec[Enum.GetValues<Animation>().Length];

        Set(table, new AnimationSpec(
            Animation.EnterPage, MotionAxis.X,
            new Track(100, 0, 1000, 0, Decel), Track.None, new Track(0, 1, 170, 0, Decel), 83, 333));

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
            Animation.CrossFadeOut, MotionAxis.None,
            Track.None, Track.None, new Track(1, 0, 167, 0, Linear), 0, 0));

        Set(table, new AnimationSpec(
            Animation.DragSourceStart, MotionAxis.None,
            Track.None, new Track(1, 1.05, 240, 0, Decel), new Track(1, 0.65, 240, 0, Decel), 0, 0));

        Set(table, new AnimationSpec(
            Animation.DragSourceEnd, MotionAxis.None,
            Track.None, new Track(1.05, 1, 500, 0, Decel), new Track(0.65, 1, 500, 0, Decel), 0, 0));

        return table;
    }

    private static void Set(AnimationSpec[] table, in AnimationSpec spec) => table[(int)spec.Name] = spec;
}
