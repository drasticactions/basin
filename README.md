# basin - The Experimental .NET Wayland Compositor

basin is an experimental Wayland Compositor, written in .NET. This is experimental and mostly a way for me to play around with getting .NET to do things it normally doesn’t.

![1444070256569233](https://user-images.githubusercontent.com/898335/167266846-1ad2648f-91c1-4a04-a18d-6dd4d6c7d21c.gif)

It's designed to be a modular library, similar to wlroots, where it’s a foundational library that other compositors can build on. It includes default implementations and dependencies that can be swapped out for others through a standard API.The goal is for the foundation to have enough hooks to let you bend it to do whatever you want, without the base having to do everything in the box. It’s built for NativeAOT (it would be insane not to make that a goal from the start) and to limit or avoid GC and allocations whenever possible.

Issues and PRs are welcome, but note that this is more of a hobby for me, so support is limited.