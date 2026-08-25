# Plasma

`src/Basin.Plasma` carries the KDE protocols the desktop pack does not. The
protocols are KDE's own surface, read out of the
`external/plasma-wayland-protocols` submodule at v1.21.0, with KWin as the
reference implementation for every semantic. The three KDE protocols that
predate this project stay in `Basin.Desktop`, and
[protocol-support.md](protocol-support.md) says which project ships each row.

## The pack is additive

`PlasmaPack.Default` is the KDE surface and nothing else. A consumer writes
`DesktopPack.Default + PlasmaPack.Default`, and a session that is not a Plasma
session pays nothing. `DesktopPack` does not change and does not grow a
dependency on `Basin.Plasma`. A `Without()` on any one global works exactly as
it does in the desktop pack.

The pack today carries the output tail of the KDE surface, the idle clock,
the key-state view, the plasma shell, the screen edges, the lock-screen
overlay, client shadows and the slide animation:

| Module | Global | Version |
|---|---|---|
| `PlasmaOutputDeviceModule` | `kde_output_device_registry_v2` | 23 |
| `PlasmaOutputManagementModule` | `kde_output_management_v2` | 21 |
| `ExternalBrightnessModule` | `kde_external_brightness_v1` | 3 |
| `OutputOrderModule` | `kde_output_order_v1` | 1 |
| `DpmsModule` | `org_kde_kwin_dpms_manager` | 1 |
| `KdeIdleModule` | `org_kde_kwin_idle` | 1 |
| `KeyStateModule` | `org_kde_kwin_keystate` | 5 |
| `PlasmaShellModule` | `org_kde_plasma_shell` | 8 |
| `ScreenEdgeModule` | `kde_screen_edge_manager_v1` | 1 |
| `LockscreenOverlayModule` | `kde_lockscreen_overlay_v1` | 1 |
| `ShadowModule` | `org_kde_kwin_shadow_manager` | 2 |
| `SlideModule` | `org_kde_kwin_slide_manager` | 1 |

The output globals, `org_kde_plasma_shell`, `kde_screen_edge_manager_v1` and
`kde_lockscreen_overlay_v1` are privileged, per the XML's
own warning that regular clients must not use them. Their names are in `PrivilegedProtocols`, which
lives in `Basin.Core` so a project outside `Basin.Desktop` can add to the
same list. `org_kde_kwin_idle`, `org_kde_kwin_keystate`,
`org_kde_kwin_shadow_manager` and `org_kde_kwin_slide_manager` are the
exceptions:
ordinary applications bind them, so their names stay out of the list. The order module rides `IOutputOrder` with a layout-derived
default, and [outputs.md](outputs.md) carries the sort rule and the
primary-output convention. The DPMS module rides `IOutputPower` beside
`zwlr_output_power_manager_v1`, and [outputs.md](outputs.md) carries the
four-modes-to-boolean mapping. The key-state module answers from the seat
directly. Its consumers are the lock screen's caps-lock warning and the
on-screen keyboard, and [input-and-seat.md](input-and-seat.md) carries its
rules.

## One capability, two protocol faces

A KDE protocol that asks a question basin already answers gets a second face
on the existing capability, never a parallel seam. The output pair rides
`IOutputConfiguration`, which `zwlr_output_manager_v1` already reads.
[services-and-capabilities.md](services-and-capabilities.md) covers the
widening, and the zwlr suite staying green unchanged is the proof the
capability widened rather than changed.

`org_kde_kwin_idle` rides `IIdleSource` beside `ext_idle_notify_v1`, and
[protocols.md](protocols.md) carries the shared idle rules. A timeout of zero
fires `idle` from an idle callback on the next loop pass. The XML does not say
what zero means. wlroots fires immediately, and KWin starts a zero-interval
timer that fires on the next pass by accident. basin picks the next pass on
purpose, because a refusal or a clamp hangs a power-management client that
polls with zero.

`zwp_text_input_v2` rides `ITextInputMethod` beside the v1 and v3 managers,
so an IME never learns which version an application speaks. Qt's Wayland
platform plugin prefers v2 when the compositor advertises it and falls back
to v3, so every Qt client in a Plasma session takes the v2 path against
KWin today. Offering v2 moves basin's Qt clients onto the same path, which
is why the v2 suite drives a v2 and a v3 client against one relay.
[input-and-seat.md](input-and-seat.md) carries the state machine.

`org_kde_kwin_server_decoration_palette` records the colour scheme a KDE
application asks its titlebar drawn in — a name in the user's config
directory, or an absolute path, and a consumer must handle both forms.
The library carries the string and does not act on it. That is
conformant: the XML says the server can choose not to follow the request,
and a compositor with one hardcoded theme answers honestly by keeping it.
Resolving a name means reading KDE's colour-scheme format out of
`~/.config`, which is a KDE convention a Wayland compositor library will
not encode. `PlasmaHost` is the consumer that closes the loop: its Breeze
frame painter resolves the string as a path or as a scheme name in the
color-schemes directories, reads the colours through KWin's own cascade, and
repaints the window's frame when the client changes the palette. The rules are
in [the colour scheme decides the theme](#the-colour-scheme-decides-the-theme).

`org_kde_kwin_appmenu` rides `IToplevelModel`: the address pair lands on
`ToplevelInfo` and `org_kde_plasma_window_management` reports it, which is
what the Global Menu applet in a plasmashell panel reads. The
`plasmawins` probe prints the address per window without the applet.

`org_kde_kwin_fake_input` rides `IInputSink` beside the virtual keyboard and
virtual pointer protocols, so all three injectors feed the seat through one
capability. KDE Connect is the consumer: its remote-input plugin drives the
pointer and keyboard from a paired phone. The gate is
`IFakeInputAuthority`, and a compositor that takes `PlasmaPack` without
registering one gets a working, inert global rather than a startup
exception. [input-and-seat.md](input-and-seat.md) carries the injection
rules and the keysym algorithm.

## KWin's stored output settings

KWin does not learn a display's scale from a protocol. It writes every
per-display setting to `~/.config/kwinoutputconfig.json` and applies that file
itself on start. kscreen and the System Settings display page edit the running
compositor over `kde_output_management_v2`, and KWin persists what they
applied. A compositor that speaks the pair and reads no file starts every
session at its backend defaults, which is what `PlasmaHost` did until this
reader landed. The symptom is a Plasma desktop at 1x on a display the same
user's KWin session drives at 1.7x.

`KwinOutputSettings` reads that file. `Locate` searches the way
`QStandardPaths` does, `XDG_CONFIG_HOME` first and then each `XDG_CONFIG_DIRS`
entry. `EntriesFor` turns the stored rows into `OutputConfigurationEntry`
values for the outputs a consumer holds, and `Apply` puts them through the
registered `IOutputConfiguration` after a `Test`. No module reads the file, and
`PlasmaPack` is unchanged. The reader is opt-in, because which file a session
trusts is the consumer's decision.

The colour-scheme rule above still holds. A palette name needs KDE's
colour-scheme format and a theme decision, so the library carries the string
and the sample resolves it. The output store carries the fields
`kde_output_configuration_v2` already carries, so reading it adds no KDE
vocabulary the pack does not already speak. The reader is the file face of a
capability that already has a protocol face.

**Matching is KWin's cascade.** A stored row is identified by its EDID
identifier, its EDID hash and its connector name. `KwinOutputSettings` derives
the first two from `IOutput.EdidBytes`: the identifier is the manufacturer,
product, serial, week, year and model year of the base block, and the hash is
the MD5 of the whole blob, which is what KWin stores. The search narrows the
same way KWin narrows it. A unique EDID identifier matches on its own. A
unique EDID hash narrows next, and a miss there ends the search. The connector
name is the last filter, never the first, because a monitor keeps its settings
across ports.

**An output with no EDID matches on its connector name alone.** This is the
one deviation. KWin compares an absent hash against an absent hash, so its
single no-EDID row matches any no-EDID display. A headless or nested basin
output then adopts the mode KWin stored for its own virtual output, and a
1920x1080 headless run silently became 1280x800. Requiring the connector name
keeps a `Virtual-0` row for an output named `Virtual-0` and leaves
`HEADLESS-1` alone.

**A setup names the layout.** The `setups` group is the per-combination half of
the file. The matching setup supplies enabled, position, priority and the
replication source, and the `outputs` group supplies everything else. A setup
matches when it covers exactly the outputs that matched a row. The best partial
cover is the fallback, where KWin generates a fresh setup instead. Setups
recorded with the lid closed are skipped, because basin has no lid state to
compare against.

**The file's units are not the protocol's.** KWin stores brightness, sharpness
and SDR gamut wideness as doubles from 0 to 1, and the protocol carries them as
0 to 10000. The minimum brightness override is nits in the file and 0.0001 nits
on the wire. The reader converts, and `KwinOutputSettingsTests` pins the
conversions, because a factor of 10000 on brightness is a black screen.

**Unsupported fields drop rather than fail.** The protocol path refuses a
configuration that asks for a feature the output lacks, and names the field in
`failure_reason`. That is right for a client that asked. A stored file
describes the hardware of whichever session wrote it, so refusing the whole
file over one HDR bit would cost the scale as well. `Apply` clears every field
`IOutputConfiguration.Supported` does not claim, and both paths share
`OutputConfigurationGate` so the field-to-feature table cannot drift. A mode
the configuration refuses costs the mode and the custom modes on one retry,
and never the scale.

**What a session applies is written back.** A change over
`kde_output_management_v2` must outlive the compositor, or the display page
appears to do nothing across a restart. `Snapshot` reads the current state of
every output the consumer holds, `Record` merges it into the store, and `Save`
writes the file. The three are the mirror of `EntriesFor`, `Apply` and the
file itself.

**The write is a merge, never a regeneration.** The reader keeps the raw text
of the file it read. The writer parses that text into a node tree and edits
only the rows and the setup that this session covers. A row for a display this
box has not connected keeps every field, and so does a setup for a
combination this session is not. A key basin does not model, such as
`autoBrightnessCurve`, survives untouched. This is what makes writing safe:
the store cannot lose what the writer does not understand. Keys are written in
the order Qt writes them, so a file that alternates between a KWin session and
a basin one shows only the fields that changed.

**A field the output cannot drive is not written.** `Snapshot` puts every
entry through the same `OutputConfigurationGate.Clear` the read path uses. A
run on hardware with no HDR therefore leaves the stored HDR bit alone rather
than replacing it with the default it was forced to. The gate is the reason
the two directions cannot disagree about which field belongs to which feature.

**A row the file does not have is appended.** The identity fields come from
the same EDID derivation the reader matches on, and the `uuid` is
`PlasmaOutputUuid.For`, which is the uuid the KDE protocols already report for
that output. The setup for the current combination is appended in the same
way. KWin reads both back.

**Replication is translated in both directions.** The file names a
replication source by the stored `uuid` of another row, and the protocol names
it by the uuid `PlasmaOutputUuid.For` gives. The reader maps the row back to
the output the consumer holds, and the writer maps it forward.

## The plasma shell

`org_kde_plasma_shell` is how plasmashell says a window is the desktop, a
panel, an OSD, a notification, a tooltip or an applet popup, and where it
goes. A plasma surface is metadata on a surface that already has a role,
almost always `xdg_toplevel`. The plasma role never mutates the xdg role.
It selects a placement.

The placement is mechanism. `PlasmaShellPlacement` owns one scene tree per
role, and the roles map onto the layers `zwlr_layer_shell_v1` already uses:

| Role | Layer | Focusable by default |
|---|---|---|
| `normal` | the window stack | yes |
| `desktop` | Background | no |
| `panel` | Top | no, `set_panel_takes_focus` flips it |
| `appletpopup` | Top, above panels | `panelTakesFocus` |
| `notification` | Overlay | `panelTakesFocus` |
| `tooltip` | Overlay, above notifications | never |
| `criticalnotification` | Overlay, above tooltips | `panelTakesFocus` |
| `onscreendisplay` | Overlay, at the top | never |

The sub-order within a layer is stable and is part of the mechanism. It is
KWin's own layer order read out of `belongsToLayer`. OSD and tooltip ignore
`set_panel_takes_focus` because KWin hardcodes exactly that, and a session
where the volume OSD steals focus is a broken session. Whether a click on a
desktop surface focuses it stays with the consumer, which reads `Focusable`
off the surface.

`set_position` is layout coordinates, not output-relative. The XML's own
example places 50,50 on a second output as 1970,50. A position sent before
the first commit applies at the first commit. A surface with no position
centers on its output, and a desktop covers its output from the origin.

`set_panel_behavior` is accepted, parsed and recorded, and nothing reads it.
Plasma 6 deprecated the request and the XML says setting it has no effect,
so acting on it would produce a panel unlike every other Plasma 6 session.

A visible panel shrinks the usable area of its output the way a layer
surface with an exclusive zone does. The anchored edge is the edge nearest
the panel's own geometry, and the strip runs from that output edge to the
panel's far edge.

`panel_auto_hide_hide` hides the panel without unmapping it: the scene node
is disabled, the surface stays mapped with its buffers held, and the
reserved strip is released. The compositor answers
`auto_hidden_panel_hidden` — also when it could not hide, because the event
doubles as that answer and the request is never left silent.
`panel_auto_hide_show` reverses all of it and answers
`auto_hidden_panel_shown`. A hidden panel arms `PlasmaScreenEdges` on the
edge it is nearest: a pointer that dwells within a logical pixel of that
border for 150 ms, or a press against it, reveals the panel and sends the
shown event. The dwell is KWin's own reactivation delay, and the edge
belongs to one output, because a border of the layout is not a border of
every display. A session with no seat never reveals, and the protocol has no
event to report that, so the panel stays hidden until the client asks —
the same outcome KWin produces. Both auto-hide requests on a surface whose
role is not `panel` raise `panel_not_auto_hide`, the one error this
protocol has.

`open_under_cursor` is recorded only when it precedes the first buffer, and
acted on at the first commit: the surface is placed at the pointer and
clamped into the output the pointer is on. Acting at request time would
place at the pointer's position several hundred milliseconds early. With no
pointer the surface places normally and nothing is reported.

`set_panel_takes_focus` flipping to true on a mapped surface passes keyboard
focus to it, which is what makes a panel's search field work. The driver
never takes focus away.

Destroying the plasma surface unmaps the wl_surface — the XML says so
explicitly, and it is the opposite of what every other add-on protocol
does. The skip bits reach `ToplevelState.SkipTaskbar` and `SkipSwitcher`,
which `org_kde_plasma_window_management` reports as `skiptaskbar` and
`skipswitcher`.

The XML says the global "can only be bound one time". basin enforces
nothing there: the XML defines no error for it, and a second bind that
fails silently is worse than a second bind that works. The manager tracks
the most recent binder for diagnostics only.

## Auto-hide for layer surfaces

`kde_screen_edge_v1` is the same auto-hide for layer surfaces. A plasma
panel uses `panel_auto_hide_hide` above. A Plasma 6 panel is a layer
surface and uses this protocol instead. Both paths arm the one
`PlasmaScreenEdges` watcher, so the edge detection cannot drift between
them.

The protocol is one edge object per (surface, border) and no events. The
client learns everything by watching its own surface. All three XML errors
are raised. `invalid_border` answers a border outside 1..4. `invalid_role`
answers any surface that is not a `zwlr_layer_surface_v1` — the check is
the role, not whether the surface looks like a panel. `already_constructed`
answers a second edge on one surface. KWin skips that last check and
creates the duplicate. basin raises it, because two edges on one surface
have no defined behaviour.

`activate` hides without unmapping: the scene node is disabled, the
surface stays mapped with its buffers held, and `LayerArrangement` skips
the disabled node so the exclusive zone is released. Unmapping instead
would make the client redraw on every reveal, and the protocol gives it no
event to learn its state from, so its own idea of the surface must stay
true.

The trigger is compositor policy, per the XML. basin's policy is the three
triggers `PlasmaScreenEdges` carries: a pointer that dwells within a
logical pixel of the armed border for 150 ms, a press against it, which
fires with no dwell because a deliberate click is unambiguous, and a
touchscreen edge swipe from the armed border through `EdgeSwipeGesture`.
The dwell is KWin's own reactivation delay, and it is what stops a panel
flashing every time the pointer crosses the edge on its way somewhere
else. A trigger reveals and **disarms**. Only another `activate` re-arms,
which is the XML's own one-shot rule, and re-arming automatically is the
behaviour users file bugs about. The edge belongs to the layer surface's
own output, because a border of the layout is not a border of every
display.

A compositor with no scene accepts `activate` and hides nothing. A session
with no seat hides and never reveals until `deactivate`. Neither is an
error and the protocol has no event to report either, which is the same
outcome KWin produces. Destroying an active edge reveals, and destroying
the manager leaves existing edges working — the XML says so explicitly.

## Above the lock screen

`kde_lockscreen_overlay_v1` lets a client nominate one of its surfaces for
display while the session is locked. The XML names the use case narrowly:
phone calls and alarms. `allow` is a request to be considered, not a grant.
The manager records the surface into `LockOverlaySurfaces` and seeds it as
the `ILockOverlaySurfaces` default, so a consumer with a stricter policy
registers its own and wins. Nothing is drawn until a consumer reads the
capability in its own lock handling. basin's default posture is that a locked
session shows the locker and nothing else.

`allow` must arrive while the surface is unmapped, and a mapped surface
raises `invalid_surface_state`. The rule is a security check, not a
formality. The compositor learns about the permission before the surface has
content, so a client cannot map something ordinary, get it composited, and
then promote it over the lock screen. The permission is on the surface for
its lifetime. A surface that maps, unmaps and maps again stays allowed.
Destroying the manager does not revoke — the XML says so, and it is the
opposite of what a manager-scoped permission usually means. Destroying the
surface is the only removal.

This is the privileged entry where the listing matters most. A sandboxed
client that can reach the global can put a surface over the lock screen the
moment the compositor honours the list. A compositor that honours the list
must keep the global away from sandboxed clients through its global filter.
`PlasmaHost` deliberately takes the permissive path so the seam is exercised:
while locked it raises every allowed mapped surface into its lock tree.

## Client shadows

`org_kde_kwin_shadow` is a client-supplied drop shadow: eight buffers arranged
as a nine-patch around the window, and four offsets that say how far the
shadow's own edges sit outside the surface. Qt sets one on menus, tooltips
and every window with client-side decorations, which is how a Plasma session
gets one consistent shadow instead of a shadow per toolkit.

The protocol has two commits, and they are not the same commit. The shadow
object's own `commit` applies its pending buffers and offsets to its current
state. Only the fields set since the last commit move, tracked by a flags
word, so a client that attaches one buffer and commits keeps the other
seven. `create` and `unset` are double-buffered on the surface instead. They
set the surface's pending shadow, and that takes effect at the next
`wl_surface.commit`. A client that calls `org_kde_kwin_shadow.commit` and
commits nothing on its surface behaves correctly and must see nothing yet.
The attachment rides a `SurfaceState` extension slot, the same mechanism
explicit sync and presentation-time use — see [surfaces.md](surfaces.md).

Every attached buffer is locked at attach, and a replaced buffer is unlocked
exactly once — [buffers.md](buffers.md) carries the lock discipline.
Destroying the shadow object removes the shadow immediately and releases
every lock. The XML names this as the one path that is not double-buffered
and recommends `unset` instead.

`ShadowEffect` is the drawing, and the consumer wires one per surface scene
the way `PlasmaHost` does. A compositor that wires nothing still tracks the
state and releases the locks, and the client sees no shadow. That is
conformant, because the protocol has no event and no error.
[rendering.md](rendering.md) carries the nine-patch geometry and the scale
rule.

### Decoration shadows

A client shadow is the client's. A server-decorated window has no client to set
one, and KWin draws the decoration's shadow instead. `createShadowFromDecoration`
is tried before the Wayland one, so a decoration shadow wins where both exist.
PlasmaHost follows that split: `ShadowEffect` draws what a client sets, and
`DropShadowEffect` from `Basin.Effects` draws the frame's own — see
[effects.md](effects.md).

`BreezeShadow` reads breezerc's `[Common]` group for `ShadowSize`,
`ShadowStrength` and `ShadowColor`, and maps the five sizes onto Breeze's own
layer parameters. A window that is not server-decorated gets nothing, and a
maximized or fullscreen one hides the shadow it has. Textures are cached per
output scale and per active state, because Breeze draws an inactive window's
shadow at half strength.

## The frame is Avalonia

PlasmaHost's Breeze decoration was one `IFrameRenderer` painting one surface
through `Basin.Scene.Frame`. It is four Avalonia surfaces now, placed as
`UISurfaceNode`s in the window's own scene tree with data-bound models, and
`IFrameRenderer`, `Frame` and `BreezeFrameRenderer` are gone from the sample.

That is the branch [promotions.md](promotions.md#westonias-window-frame)
refused for Westonia, taken from the other side. Rather than force a toolkit
into `Frame`'s one-surface-one-buffer contract, PlasmaHost stops using `Frame`.
What it buys is the whole synchronous problem: `Frame.Configure` calls the
renderer's `Draw` and then `TryAcquire` on the next line, which Skia satisfies
because it paints synchronously and Avalonia cannot, because Avalonia schedules
a dispatcher operation and would hand back the previous frame forever. Nothing
here acquires a buffer synchronously, so no seam has to force a toolkit render.

What it gives up is stated plainly. Resize is no longer atomic: four surfaces
reach the scene independently and there is no `HasPendingFor` to wait on, so a
strip can lag or lead the client by a frame during a drag. There are four
Avalonia control roots per framed window rather than one Skia surface. The
system menu is rewritten as an Avalonia `MenuFlyout`, which becomes a popup
top-level that `UIDriver` places in the plasma overlay layer and
`UISurfaceRouter` routes the pointer into.

Hit testing stays the compositor's and stays arithmetic — no toolkit call on
the input path. `BreezeMetrics` is the one record both halves read: its
`LayoutButtons` places the buttons on the titlebar's canvas *and* answers which
button a pointer is over, so the drawn layout and the hit test agree by
construction rather than by living in one file.

Two positions per strip, not one. The scene node is placed in tree-local
coordinates and the Avalonia surface in scene coordinates, because Avalonia
positions a popup from its anchor's screen position. Moving a window moves the
tree and not the surface, so a move syncs them; without that the window menu
opens where the window used to be.

The four KWin shells that are QML upstream are Avalonia here too. Overview,
the grid variant and window view are one class with a mode, because they differ
only in which windows they collect and whether the desktop bar is drawn. The
thumbnails are `SceneMirror` nodes above the Avalonia backdrop rather than
anything the toolkit draws. TilesEditor edits a tile tree PlasmaHost defines,
and reads and writes kwinrc's `[Tiling][<output>]` entry in KWin's own JSON so
a layout survives a restart. DesktopChangeOsd is one surface and a timer.

Breeze's two decoration animations are Avalonia property transitions at the
duration breezerc names, which is what the models being data-bound buys. The
shadow is outside Avalonia — it is a `DropShadowEffect` in the scene — so it
crossfades by ramping two effects' opacity between the two textures the cache
already holds, rather than re-rasterizing per frame.

## The colour scheme decides the theme

KWin has no light mode and no dark mode. It has the user's colour scheme, and
light or dark is a property of that scheme. PlasmaHost reads the scheme the
same way and takes no theme flag, because a Plasma session has no such switch
to honour.

**The default palette is `kdeglobals` itself, not the scheme file.** Applying a
scheme in System Settings copies its colour groups into `kdeglobals` and
records the name under `[General] ColorScheme`. KWin's `DecorationPalette`
opens `kdeglobals` and reads the colours out of it. A consumer that resolves
the name to `<name>.colors` instead misses every per-user edit. It also falls
back to hardcoded Breeze light on a machine whose `kdeglobals` carries no
`ColorScheme` key. PlasmaHost opens `kdeglobals` for the default palette, and
resolves a name or an absolute path only for the per-window palette a client
asks for.

**The titlebar colours are a cascade, not `[WM]` alone.** `BreezePalette.Load`
follows KWin. It reads `[Colors:Header]` when the scheme has that group, with
`[Colors:Header][Inactive]` for the unfocused titlebar. It reads the legacy
`[WM]` keys when the scheme has no header set, and `[Colors:Window]` when it
has neither. Breeze ships both groups and they disagree. Its `[Colors:Header]`
active background is `222,224,226` and its `[WM] activeBackground` is
`227,229,231`, which is the *inactive* header colour. A consumer that reads
`[WM]` first paints every focused window in the colour KWin gives an unfocused
one.

**Light or dark is one test on one colour.** KWin's overview effect derives
`lightBackground` from `Math.max(r, g, b) > 0.5` of the scheme's window
background. `BreezePalette.IsDark` is that test. It decides the Avalonia theme
variant, and nothing else reads a mode.

**The variant is per window, because the window menu is.** KWin paints the
window menu in the window's own decoration palette, so a window that asked for
a dark scheme gets a dark menu in a light session. PlasmaHost sets
`RequestedThemeVariant` on each frame strip's Avalonia root, and the
`MenuFlyout` popup inherits it through the anchor. The strips bind explicit
brushes and do not need the variant themselves. The one Fluent-templated
control in the chrome is that menu, which is why a hardcoded variant was
invisible everywhere else.

**The overlays take the scheme's colours too.** Overview, window view and the
tiles editor paint `Kirigami.Theme.backgroundColor` under KWin, and the desktop
OSD is a Plasma dialog. None of them is dark by nature. `ShellBrushes` builds
every overlay brush from `[Colors:Window]` and `[Colors:Selection]`, at the
alpha each surface already used. The alpha is deliberately unchanged.
PlasmaHost draws thumbnails over an opaque backdrop where KWin blurs the
desktop behind a translucent one, so the opacity belongs to this sample's
compositing rather than to the theme.

**A change arrives over the bus, not from the file.** `KConfigWatcher` is a
DBus subscriber. A writer that passes `KConfigBase::Notify` emits
`ConfigChanged` on the `org.kde.kconfig.notify` interface, at a path built from
the config's file name. The colours KCM does this and a text editor does not,
so KWin itself never sees a hand-edited `kdeglobals`. `KdeConfigNotify`
subscribes to that signal for `/kdeglobals` and ignores the body, which is what
`DecorationPalette::update` does. The signal arrives on the DBus reader thread,
so it wakes the compositor over a pipe the event loop owns, and the reload runs
on the compositor thread. A session with no bus keeps the colours it started
with.

Only `/kdeglobals` is watched. KConfig emits nothing for a config opened by
absolute path, and a scheme in `~/.local/share/color-schemes` is not in a
config directory, so no signal can exist for a per-window scheme file. KWin
watches those files and gets the same silence.

**The switch cross-fades, the way KWin's does.** `BlendChangesStage` in
`Basin.Effects` is the port of KWin's `BlendChanges` effect, and PlasmaHost
already built it for the `blend` stdin command. The reload captures the scene
before it applies the new palette, hands that frame to the stage, and the stage
fades it out over the new one in 400ms on an InOutCubic curve. That is
`animationTime(400ms)` and `QEasingCurve::InOutCubic` upstream. KWin starts the
effect when plasma calls `org.kde.KWin.BlendChanges.start` on the session bus.
PlasmaHost owns no DBus name, so the notify that changed the colours starts it
instead.

A reload runs only when the colours moved. A `kdeglobals` notify fires for any
key in the file, and a fade on an unrelated write is a visible 400ms glitch.
`ReloadTheme` loads the default palette, compares it to the one in hand, and
returns when the two are equal. `BreezePalette` is a record struct, so that one
comparison covers every colour.

**The fade is per output, and getting there took three fixes.**
`BlendChangesStage` holds one captured frame and `PostContext` carries no output
identity, so one shared stage can only fade one screen. `PlasmaHostStages` now
keeps a stage for each `SceneOutput`, creates it on `Attach` and disposes it on
`Detach`, and builds each output's post-stage list separately. The shared
`_attached` list is gone. Each output also gets its own live list, so an output
attached after the first sync is no longer left with no stages at all.

`Scene.Render` always collected from the scene origin, so a capture for the
second output held the first output's pixels. `SceneRenderOptions` gained
`OriginX` and `OriginY` in logical scene coordinates, which `Scene.Render`
subtracts when it collects the tree. That is what `SceneOutput` already did with
`CollectRenderList(list, -position.X, -position.Y)` — the same offset, reachable
from outside. `PlasmaHost` passes each output's layout box when it captures, and
`WriteScreenshot` passes it too, which fixes `shot` on any output that is not at
the origin. Both `shot` and `shotraw` now take an optional output name, because
a fade across two screens cannot be checked from one of them.

**A running post stage must damage its output.** The scene does not change while
a fade runs, so `Ring` stayed empty, `NeedsRepaint` was false, and the post stage
rendered exactly one frame. The old frame then sat at full opacity for the whole
duration and cut to the new one at the end. `PlasmaHostStages.NeedsFullRepaint`
answers whether an animating stage owns the output's pixels, and the sample's
`BeforeRepaint` handler calls `Ring.AddWhole()` when it does. `BeforeRepaint`
runs at the top of `SceneOutput.Commit`, before the ring is read, so the damage
lands on the same frame. Without it the `blend` command had always looked like a
hold and a cut rather than a fade.

## Restoring a maximized window

A maximized window remembers where it was. `PlasmaHostView.RestoreGeometry`
holds the frame rectangle from the moment before the window was maximized or
made fullscreen, and unmaximizing puts it back there and asks for that size
again. That is what KWin's `geometryRestore` does, and a desktop that drops a
window somewhere else on restore feels broken in a way users notice
immediately.

The obvious implementation is the wrong one, and it is worth naming because it
looks right. Unmaximizing by re-running the initial placement rule lands every
window in the corner: at the moment the restore is decided the surface still
carries its *maximized* size, so a placer that centres a window of that size
inside the usable area computes a negative offset, clamps it, and puts the
window at the top left. The window then shrinks in place and stays there.

The rectangle is saved once, on the transition out of a normal state, so
maximize into fullscreen and back out again returns to the maximized geometry
first and to the original rectangle second, rather than losing it at the first
step. A window with no saved rectangle -- one that was mapped maximized --
falls back to the placer, which is the only case where the placer is the right
answer.

**Dragging the titlebar of a maximized window restores it under the cursor.**
KWin does this in `Window::handleInteractiveMoveResize`: on a move, if the
window is maximized and the next geometry differs along a maximized axis, it
calls `maximize(MaximizeRestore)` and returns, and the next motion places the
now smaller window. The part that makes it feel right is that
`interactiveMoveOffset` is a *fraction* of the window, not a pixel offset:
`nextInteractiveMoveGeometry` puts the top left at
`anchor - offset * currentSize`, so the pointer keeps the same proportional
place on a titlebar that just became half as wide.

PlasmaHost's move grab stored the offset in pixels, which is why the window
could not be pulled loose at all. It is a fraction of the *outer* box now,
decorations included, as KWin's is of the frame geometry, and the first motion
of a drag on a maximized window calls `RestoreForDrag`. That unmaximizes,
places the restored outer box so the same fraction sits under the pointer, and
hands the stretch its two rectangles, so the window shrinks toward the cursor
while the drag carries on.

One thing the fraction cannot fix on its own. `SetSize` only asks, so the
window still reports its maximized size until the client commits, and a
fraction of that size puts the window hundreds of pixels from the pointer for
those few frames. The grab therefore keeps the size it expects and distrusts
the reported one until it changes.

**A drag takes no hold.** The stretch waits for the commit and parks the window
where it was until then, which is right when the compositor moved a window the
user was not touching. It is wrong here. The hold pins the drawing to a fixed
point in space, so a window pulled off the top of the screen stayed drawn at
full size in the corner until the client got round to acking — a quarter of a
second of a maximized titlebar sitting at the top while the pointer dragged an
invisible window away from it. A slow client made it longer and a quick one
hid it, which is why it only happened sometimes.

`RestoreForDrag` knows the rectangle it just placed, so it does not need the
commit to tell it. It starts the size animation itself, with no hold, and the
window leaves the corner on the first frame. That is also what upstream does:
its `oldGeometry` is the maximized rectangle and its translation carries the
window from there to the cursor across the animation, rather than waiting and
then starting.

A window mapped maximized has no saved rectangle. Rather than refuse the drag
it unmaximizes at its current size, so it comes loose under the cursor and the
stretch declines because there is nothing to scale.

**A resized strip must be resized on its node, not only on its surface.**
`UISurfaceNode.Configure` is the call that moves `DestinationWidth` and
`DestinationHeight`; `AvaloniaUISurface.Configure` only tells the toolkit. The
frame placer called the second, so between a resize and the toolkit's next
published frame the node still drew the previous buffer *at its previous size*.
For a titlebar going from a maximized 1920 to a restored 1031 that is the
maximized titlebar, drawn full width, sitting where it was. Going through the
node moves the destination immediately, so the old buffer is scaled into the
new box for those frames instead.

Nothing about this is specific to the drag. It is every strip resize, and how
long it shows is how long the toolkit takes to publish — a frame on an idle
headless run against the software renderer, longer on a real session where the
chrome renders into a dmabuf and plasmashell is competing for the GPU. That is
the whole of its "sometimes". `PlasmaShellSurface` carried the same call and
has the same fix.

## Sliding surfaces

`org_kde_kwin_slide` asks the compositor to animate a surface in from a
screen edge instead of having it appear. Plasma sets it on panels, on the
notification popup and on the applet popups that slide out of a panel.
`location` names the edge the surface comes *from*, so a popup anchored to
the bottom with `location = top` slides down from above, which looks wrong
and is correct. `offset` is the distance from the screen edge where the
animation begins: zero starts flush with the edge, and a positive offset
starts further out.

The commit structure is shadow's exactly: the slide object's own `commit`
applies its pending location and offset, and `create` and `unset` are
double-buffered on the surface. The manager has no destroy request at all —
a version-1 protocol showing its age — so its resource lives as long as the
client, and a teardown that assumes otherwise leaks it.

basin's animation is 250 ms on the same ease-out curve the workspace slide
uses, run by `SlideEffect` where the consumer wires one, as `PlasmaHost`
does. The animation begins when the surface maps with a slide attached, and
also when a slide first lands on a surface that is already mapped —
plasmashell attaches the slide to an applet popup about 100 ms after the
popup's first commit, and KWin animates that arrival, so basin must too. **Only the incoming slide is guaranteed by the XML.** It says nothing
about the way out. Plasma expects the reverse and KWin does it, so basin
does it too, and that outgoing slide is basin's choice rather than the
protocol's. [scene.md](scene.md) carries the buffer hold that choice
requires and the destroy-cancels rule.

## The backdrop: blur and contrast

KWin 6.7.80 dropped `org_kde_kwin_blur` for `ext-background-effect-v1`, which
basin implements in `Basin.Desktop`. The KWindowSystem that ships with Plasma
6.7.4 still binds the KDE one, and `org_kde_kwin_contrast` has no `ext-`
successor at all, so `Basin.Plasma` carries both.

**The precedence is per surface.** A surface with an `ext-background-effect`
blur region uses it and its KDE blur region is ignored, because a client that
has moved to the ext protocol should not be blurred twice through two
descriptions of the same wish. Contrast has no competitor, so it always
applies. An unset region on either means the whole surface, which is the
protocol's own default and is not the same as an empty one — `SurfaceBlur`
carries `WholeSurface` beside the region rather than letting an empty region
stand for both.

Both double-buffer twice over, like the shadow and the slide: `set_region`
stages on the blur or contrast object, `commit` on that object stages it
against the surface, and the surface's own commit applies it.

Neither is privileged, and neither errors without a backend. The global
installs, the region is recorded, and `BlurRegionOf` and `ContrastRegionOf`
return nothing, so a compositor with no GPU blur draws an unblurred panel and
the client is none the wiser. See
[protocol-support.md](protocol-support.md).

## What PlasmaHost reads out of kwinrc

One reader, in the shape of the `KdeIni` reader the sample already used for
`breezerc`.

| File | Group | Key | What it decides |
|---|---|---|---|
| `kdeglobals` | `[KDE]` | `AnimationDurationFactor` | Every effect's duration, and zero turns them all off |
| `kwinrc` | `[Plugins]` | `<name>Enabled` | Whether an effect is built at all, defaulting to that effect's own upstream default |
| `kwinrc` | `[Effect-blur]` | `BlurStrength`, `NoiseStrength`, `Saturation` | The backdrop |
| `kwinrc` | `[Effect-glide]` | the eight in and out parameters | glide's poses |
| `kwinrc` | `[Effect-scale]` | `Duration`, `InScale`, `OutScale` | The open and close zoom |
| `kwinrc` | `[Effect-fallapart]` | `BlockSize` | The cell size |
| `kwinrc` | `[Effect-zoom]` | the eight tracking and upscaler keys | The full-screen zoom |
| `kwinrc` | `[Effect-magnifier]` | `Width`, `Height` | The lens |
| `kwinrc` | `[Effect-colorblindnesscorrection]` | `Mode`, `Intensity` | The correction |
| `kwinrc` | `[Effect-diminactive]` | `Strength` | The dim |
| `kwinrc` | `[Effect-mouseclick]` | `RingLife`, `RingSize`, `RingCount`, `LineWidth` | The click rings |
| `kwinrc` | `[Effect-shakecursor]` | `TimeInterval`, `Sensitivity`, `Magnification`, `OverMagnification` | The shake |
| `kwinrc` | `[Effect-startupfeedback]` | `Timeout` | How long the busy indicator waits |
| `kaccessrc` | `[Bell]` | `VisibleBellPause` | The visual bell, floored at 200ms |

The enablement defaults differ per effect and are each one's own upstream
`metadata.json` value. scale, squash, maximize, zoom, blendchanges,
screentransform, shakecursor, systembell and startupfeedback are on; glide,
sheet, fade,
fallapart, magiclamp, diminactive, invert, magnifier, showpaint,
colorblindnesscorrection, mouseclick, mousemark, trackmouse and touchpoints
are off.

The colour groups in `kdeglobals` are not in this table. `BreezePalette` reads
them on its own path — see
[the colour scheme decides the theme](#the-colour-scheme-decides-the-theme).

## Which effect animates a window

Several effects want the same event, and KWin resolves it not by a priority
list but by each effect's own eligibility test plus its enablement. PlasmaHost
does the same, and the table is here because it is the part a reader will
otherwise assume is arbitrary.

| Event | Checked in this order | Eligibility |
|---|---|---|
| Open, close | sheet, glide, scale-or-fade | The first enabled one whose test passes takes the window |
| Close only | fallapart, then the above | fallapart applies to real windows, never to popups or docks |
| Minimize, restore | magiclamp, then squash | squash refuses without a reported taskbar rectangle |
| Maximize, restore | stretch | Nothing competes: upstream marks it the sole effect in the `maximize` category |

Sheet goes first because its test — the window is modal — is strictly the
narrowest. KWin's own order is plugin load order, which is not a thing basin
can reproduce, so this order is basin's and is written down for that reason.

The eligibility test is transliterated with its class rules:

- `plasmashell` windows are animated only when they carry a decoration. That is
  KWin's heuristic for telling a settings dialog from a panel, and the two
  share one window class.
- `ksmserver`, its logout greeter and `ksplashqml` are never animated here,
  because the logout and login effects own them upstream.

**The minimize target is a reported rectangle, not a guess.** A taskbar reports
where a window's entry sits with `org_kde_plasma_window.set_minimized_geometry`,
relative to the panel that reported it. `PlasmaWindowManager` keeps one entry
per panel surface, newest wins — which is what KWin's `iconGeometry()` does —
and pushes it down as a request carrying the panel surface and the panel-local
box. Only the compositor knows where that panel sits, so PlasmaHost translates
it through the panel's scene position and answers with
`SetMinimizedGeometry`. When no panel has reported one, the effect falls back
to KWin's own rule: the nearest border if the cursor is inside the window, the
side it is on if not.

Which panel edge a reported icon lies on is inferred the way KWin infers it,
from the icon's proportions and where its centre sits on its screen.

## Screencast keeps PipeWire out of the library

`zkde_screencast_unstable_v1` answers a stream request with a PipeWire node,
and every pixel travels over PipeWire. `Basin.Core` still gains no PipeWire
binding and no PipeWire type. The reason is not the binding — `pipewire-dotnet`
exists, loads `libpipewire-0.3` by soname and degrades by name the way
`Basin.Video.FFmpeg` does. The reason is that a PipeWire stream is not just a
node. It is a format negotiation, a buffer pool that can be dmabuf or memfd,
a cursor metadata channel and a lifetime tied to a session the portal owns.
Every one of those is a choice, and a library that makes them has taken
policy. `IScreencastPublisher` is the seam instead: the module resolves the
request, the consumer publishes the node and pulls pixels through
`IScreenCapture`. That is `Basin.Screencast`'s job if it is ever written, and
the seam does not change if it is. `PlasmaHost` proves the seam is
implementable from the package alone — see [samples.md](samples.md).

## Honesty over completeness

The device advertises version 23. The connector preferences — overscan, vrr
policy, rgb range and max bits per color — have a mechanism on DRM and their
bits flip where the connector carries the property. The color cluster — HDR,
wide color gamut, the ICC sources, brightness and dimming — rides
`ColorOutputConfiguration` from [color.md](color.md) when the consumer takes
`ColorCapabilityPack`. Hardware brightness is delegated the way KDE itself
delegates it: setting a monitor's brightness over DDC/CI is slow, flaky and
needs i2c access, so powerdevil does the work and offers it back through
`kde_external_brightness_v1`. Custom modes generate through libxcvt on DRM, and sharpness and
ABM ride their amdgpu properties where a connector carries them. Replication follows the
reference's subtractive shape and ships with an aspect-fit default — see
[outputs.md](outputs.md). Auto rotate rides iio-sensor-proxy on internal
panels, and EDR raises the backlight for HDR content on SDR panels — see
[color.md](color.md). The color power tradeoff is a recorded preference that
forces ABM off in its accuracy setting. The rule is to
advertise the version and report each field honestly. The capability bit stays
clear unless a consumer says otherwise, the event carries the protocol's own
neutral value, and a configuration request for the field fails with a
`failure_reason` naming it. kscreen then sees a display without the feature,
which is true. It never sees a compositor that accepted a change and did
nothing.

## Verification

`scripts/run-plasma.sh` runs the whole desktop: plasma-host or kwin_wayland,
a private session bus with the display exported, and plasmashell on top. The
private bus is the load-bearing part. DBus-activated services inherit the
bus daemon's environment, so the display must be exported before the daemon
starts, or kactivitymanagerd aborts in a loop and plasmashell hangs
windowless. The same rule broke the portals earlier — see
[diagnostics.md](diagnostics.md). The kwin leg exists so a divergence
between the two compositors is one command away from either side.

kwin_wayland binds its Wayland socket before it probes its backend, so the
socket appearing does not mean the compositor came up. A run whose backend
fails leaves the socket bound for a moment and then exits, and a shell
started against that socket aborts in Qt with no platform plugin and dumps
core. The script therefore waits after the socket appears and reports the
compositor's exit status instead of starting the shell. kwin writes its
diagnostics to the journal and not to stderr, so the script prints the tail
of `journalctl --user -t kwin_wayland` with that report.

`scripts/compare-plasma.sh` measures plasma-host against kwin_wayland. The
coverage section diffs every advertised global by name and version, and its
MISSING section is the honest gap list: as of 2026-08-24 that is
`frog_color_management_factory_v1`, `org_kde_plasma_activation_feedback`,
`zwp_input_method_v1` and `zwp_input_panel_v1`, with `zwp_text_input_manager_v3`
one version behind. The smoke section proves a real KDE client maps on both.
The bench section runs the whole desktop on each and reports PSS per process.
The rows are plasma-host, plasma-host-aot for the NativeAOT publish, and kwin.
The AOT row advertises the same 87 globals and passes the same smoke, and it
holds the settled desktop at 179 MB PSS to CoreCLR's 194 and kwin's 234.
kwin makes itself non-dumpable, so its `smaps_rollup` is root-owned and the
sampler needs passwordless sudo to see it.

kscreen-doctor is the whole KDE output stack in one command, and it prints
every property it read:

```sh
dotnet run --project samples/TinyComp -c Release -- --outputs 2 &
WAYLAND_DISPLAY=wayland-1 kscreen-doctor -o
WAYLAND_DISPLAY=wayland-1 kscreen-doctor output.HDMI-A-2.position.1920,0
```

A failed apply prints the `failure_reason` string verbatim, which is the
fastest check of the honesty rule. `kcmshell6 kcm_kscreen` reads the
properties kscreen-doctor does not. A mode change on a real connector needs a
DRM run, because neither a nested nor a headless run changes a real mode.

A theme switch is verifiable headless, because the notify signal is an
ordinary session-bus signal that any writer can emit. Point the run at a
scratch config, drive it over stdin, and flip the scheme under it:

```sh
mkdir -p /tmp/kcfg && cp /usr/share/color-schemes/BreezeLight.colors /tmp/kcfg/kdeglobals
mkfifo /tmp/in && exec 3<>/tmp/in
XDG_CONFIG_HOME=/tmp/kcfg plasma-host --backend headless --shell false \
    --renderer pixman < /tmp/in &
echo "shot /tmp/light.png" >&3
cp /usr/share/color-schemes/BreezeDark.colors /tmp/kcfg/kdeglobals
XDG_CONFIG_HOME=/tmp/kcfg kwriteconfig6 --notify --file kdeglobals \
    --group General --key BasinStamp "$RANDOM"
echo "shot /tmp/dark.png" >&3
```

The stamp key is the trap. `kwriteconfig6` writes and notifies only when the
value changes, and a `.colors` file already carries
`[General] ColorScheme=BreezeDark`, so the obvious command is silent. Copy the
colours first and notify with a key that is new.

`kwriteconfig6` is only half of what KDE sends, and the other half is what
repaints the *clients*. `plasma-apply-colorscheme` emits three things: a
`BlendChanges.start` method call to `org.kde.KWin`, the `ConfigChanged` signal
on `/kdeglobals`, and the legacy `notifyChange` signal on `/KGlobalSettings`.
The compositor's chrome follows the second. A Qt client repaints on the third,
through plasma-integration, so a client started without
`QT_QPA_PLATFORMTHEME=kde` stays in its old colours no matter what the
compositor does. A test that flips the scheme with `kwriteconfig6` therefore
shows dark window frames around a light Konsole, which looks like a compositor
bug and is not one. Use `plasma-apply-colorscheme` when the whole session is
supposed to move, with `QT_QPA_PLATFORM=offscreen` if the shell running it has
no display of its own.

`shot` and `shotraw` both take an optional output name, and they answer
different questions. `shot` re-renders the scene, so it never shows a post
stage. `shotraw` writes the buffer that was presented, which is the only way to
see the cross-fade at all.

The bus is the other half of a real-session check. `run-plasma.sh` creates its
private session bus before it starts the compositor, and publishes the display
to that bus with `dbus-update-activation-environment` once the socket is known.
It used to create the bus after the compositor, which left the compositor on the
outer bus while System Settings, plasmashell and the KCMs ran on the private
one. Every notify then went to a bus nothing was listening on, and changing the
colour scheme in System Settings did nothing at all.

The appmenu and palette paths have real-client recipes that need no
plasmashell. Qt exports a window's menu only when the session bus carries
`com.canonical.AppMenu.Registrar`, and `dbus-test-tool black-hole
--name=com.canonical.AppMenu.Registrar` owns the name well enough:

```sh
dotnet run --project samples/PlasmaHost -c Release -- --shell false &
dbus-run-session -- sh -c '
  dbus-test-tool black-hole --name=com.canonical.AppMenu.Registrar &
  sleep 1; WAYLAND_DISPLAY=wayland-0 konsole'
WAYLAND_DISPLAY=wayland-0 scripts/wlclients/bin/plasmawins   # appmenu ":1.x" "/MenuBar/N"
```

The palette needs plasma-integration, so `QT_QPA_PLATFORMTHEME=kde`, and an
application whose config carries a per-app scheme — kwrite with
`[UiSettings] ColorScheme=BreezeDark` in a scratch `XDG_CONFIG_HOME` sends
`set_palette` with the scheme's absolute path, and PlasmaHost's `where`
prints it per window. Two traps: the string arrives as a path here and as a
bare name elsewhere, so a consumer must take both, and
`QT_QPA_PLATFORMTHEME=kde` blocks before the Wayland connect on a headless
box with no seat capabilities — give the seat a keyboard first, or run the
compositor where libinput has devices.

Keysym injection through fake input is checked with a terminal capturing
its own input. `foot -- bash -c 'cat > /tmp/typed'` focused, then a client
injecting the keysyms for F, ediaeresis and a plus Return must produce
exactly `Fëa`: the capital proves the one-event modifier OR, the ë proves
the one-key keymap swap, and the trailing a proves the keymap came back. A
client that skips `wl_seat.capabilities` sees key events on a seat real
clients ignore, so check the capability bits before trusting a silent run.

`kde_lockscreen_overlay_v1` has a canonical client that needs neither a phone
nor a scheduled alarm. `kclock --alarm-lockscreen-popup <id>` runs the exact
path a firing alarm takes: it binds the global, calls `allow` on its window
while the window is unmapped, maps the popup and raises it. The id need not
name a real alarm. The popup shows "Alarm not found" and still drives the whole
overlay path. KDE Connect's desktop daemon does not link this interface, so
kclock is the faithful stand-in for the "ring the paired phone" check.
`scripts/lockscreen-overlay-kclock.sh` drives it end to end against a headless
`PlasmaHost`. It records the wire, so the `allow` request is proven rather than
inferred, then locks with `scripts/session-lock.sh` and samples the frame. The
allowed alarm surface composites above the lock, and every un-allowed surface
is the lock colour.

The output store is checked from both ends. A written file that names the
headless connector proves the parse, the setup and the apply without hardware:
`--config PATH` reads it, `--config false` reads none, and a `--scale` on the
command line still wins. The claim that matters needs a display. A DRM run of
`PlasmaHost` on a box whose KWin session stored `DP-1` at 1.7 with a
3840x2560 mode comes up at exactly that scale and mode, matched on the EDID
rather than on the connector name.

## Traps

- KWin's stored output file is read and never written. A display change made
  through kscreen against plasma-host is gone at exit.
- A stored row with no EDID is matched on its connector name, which KWin does
  not require. Without it every headless output adopts KWin's virtual display.
- Every device object is per client and per registry resource. A cached "the
  device object" is wrong the moment a second client binds.
- `done` is per burst, not per event. One `done` per property makes every
  intermediate state visible, and kscreen will act on some of them.
- A device state burst per frame silently disables the Apply button in the
  System Settings display page. [outputs.md](outputs.md) carries the rule.
- `removed` does not destroy the device. The client calls `release`, and until
  it does the object stays alive and inert.
- The uuid is a persistence key, not an identifier.
  [outputs.md](outputs.md) carries the derivation and marks it frozen.
- `sdr_brightness` defaults to 200, not 0. Zero reads as a black screen to a
  client that trusts it.
- `scale` is `fixed`, not `int`. The wlr protocol has both spellings, and a
  copied line silently becomes 1x.
- The idle event is `idle`, not `idled`. `ext_idle_notify_v1` spells it
  differently, and a copied line is silently the wrong opcode.
- A plasma role is assigned once. A second `set_role` is not an error and is
  not applied, and both halves matter.
- A hidden auto-hide panel keeps its buffers. Unmapping it instead makes
  Plasma redraw the whole panel on every reveal.
- A shadow that appears before the surface commits is the two-commits bug.
  The shadow's own commit applies buffers, and only `wl_surface.commit`
  attaches.
