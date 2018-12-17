# UnityAspectRatioController
Unity script to enforce window aspect ratio for standalone Windows 32/64bit builds.

<img src="https://raw.githubusercontent.com/DenchiSoft/UnityAspectRatioController/master/images/ex1.gif" width="372" > <img src="https://raw.githubusercontent.com/DenchiSoft/UnityAspectRatioController/master/images/ex2.gif" width="415" >

## How to use?
Add the MonoBehaviour `AspectRatioController.cs` to any GameObject in your scene (see included example scene). Then set the desired aspect ratio and minimal resolution values in the inspector.

## What does this do?
This script enforces the set aspect ratio for the Unity game window. That means that you can resize the window but it will always keep the aspect ratio you set.
 
This is done by intercepting window resize events (WindowProc callback) and modifying them accordingly.
You can also set a min/max width and height in pixel for the window. Both the aspect ratio and the min/max resolutions refer to the game area, so, as you'd expect, the window title bar and borders aren't included.

This script will also enforce the aspect ratio when the application is in fullscreen. When you switch to fullscreen, the application will automatically be set to the maximum possible resolution on the current monitor while still keeping the aspect ratio. If the monitor doesn't have the same aspect ratio, black bars will be added to the left/right or top/bottom.

Make sure you activate "Resizable Window" in the player settings, otherwise your window won't be resizable.
You might also want to uncheck all unsupported aspect ratios under "Supported Aspect Ratios" in the player settings.
 
**NOTE:** This uses WinAPI, so it will only work on Windows. Tested on Windows 10, but should work on all recent versions.
