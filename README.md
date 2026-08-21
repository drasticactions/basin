# basin - The Experimental .NET Wayland Compositor

basin is an experimental Wayland Compositor, written in .NET. This is experimental and mostly a way for me to play around with getting .NET to do things it normally doesn’t.

![1444070256569233](https://user-images.githubusercontent.com/898335/167266846-1ad2648f-91c1-4a04-a18d-6dd4d6c7d21c.gif)

It's designed to be a modular library, similar to wlroots, where it’s a foundational library that other compositors can build on. It includes default implementations and dependencies that can be swapped out for others through a standard API.The goal is for the foundation to have enough hooks to let you bend it to do whatever you want, without the base having to do everything in the box. It’s built for NativeAOT (it would be insane not to make that a goal from the start) and to limit or avoid GC and allocations whenever possible.

Issues and PRs are welcome, but note that this is more of a hobby for me, so support is limited.

## LLM Use

LLMs have been used in the codebase, mainly on tests and CI (I rather not write YAML anymore, thank you very much). The library, app, and sample code is mine. Since this project is experimental and for me to learn, I wanted to be hands on and actually write the code so I could understand it better. I write the code, then have an LLM set up the test harnesses for the remote and local computers and try to break it. Then I go in and fix it. If I'm really stuck on a specific issue (nvidia EGL support and dmabuf issues...) then I'll lean on an LLM to bash at it to get some form of answer. It's faster than trying to do it all myself, but it's not "pure."

That's to say that if you hate how this codebase looks, don't blame AI slop, blame me, lol.