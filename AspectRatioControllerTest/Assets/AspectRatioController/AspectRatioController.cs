using UnityEngine;
using System;
using System.Collections;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine.Events;

/// <summary>
/// This script enforces the set aspect ratio for the Unity game window. That means that you can resize the window but
/// it will always keep the aspect ratio you set.
/// 
/// This is done by intercepting window resize events (WindowProc callback) and modifying them accordingly.
/// You can also set a min/max width and height in pixel for the window.
/// Both the aspect ratio and the min/max resolutions refer to the game area, so, as you'd expect, the window
/// title bar and borders aren't included.
///
/// This script will also enforce the aspect ratio when the application is in fullscreen. When you switch to fullscreen,
/// the application will automatically be set to the maximum possible resolution on the current monitor while still keeping
/// the aspect ratio. If the monitor doesn't have the same aspect ratio, black bars will be added to the left/right or top/bottom.
///
/// Make sure you activate "Resizable Window" in the player settings, otherwise your window won't be resizable.
/// You might also want to uncheck all unsupported aspect ratios under "Supported Aspect Ratios" in the player settings.
/// 
/// NOTE: This uses WinAPI, so it will only work on Windows. Tested on Windows 10, but should work on all recent versions.
/// </summary>
public class AspectRatioController : MonoBehaviour
{
    /// <summary>
    /// This event gets triggered every time the window resolution changes or the user toggles fullscreen.
    /// The parameters are the new width, height and fullscreen state (true means fullscreen).
    /// </summary>
    public ResolutionChangedEvent resolutionChangedEvent;
    [Serializable]
    public class ResolutionChangedEvent : UnityEvent<int, int, bool> { }

    // If false, switching to fullscreen is blocked.
    [SerializeField]
    private bool allowFullscreen = true;

    // Aspect ratio width and height.
    [SerializeField]
    private float aspectRatioWidth  = 16;
    [SerializeField]
    private float aspectRatioHeight = 9;

    // Minimum and maximum values for window width/height in pixel.
    [SerializeField]
    private int minWidthPixel  = 512;
    [SerializeField]
    private int minHeightPixel = 512;
    [SerializeField]
    private int maxWidthPixel  = 2048;
    [SerializeField]
    private int maxHeightPixel = 2048;

    // Currently locked aspect ratio.
    private float aspect;

    // Width and height of game area. This does not include borders and the window title bar.
    // The values are set once the window is resized by the user.
    private int setWidth  = -1;
    private int setHeight = -1;

    // Fullscreen state at last frame.
    private bool wasFullscreenLastFrame;

    // Is the AspectRatioController initialized?
    // Gets set to true once the WindowProc callback is registered.
    private bool started;

    // Width and heigh of active monitor. This is the monitor the window is currently on.
    private int pixelHeightOfCurrentScreen;
    private int pixelWidthOfCurrentScreen;

    // Gets set to true once user requests the appliaction to be terminated.
    private bool quitStarted;

    // WinAPI related definitions.
    #region WINAPI

    // The WM_SIZING message is sent to a window through the WindowProc callback
    // when the window is resized.
    private const int WM_SIZING = 0x214;

    // Parameters for WM_SIZING message.
    private const int WMSZ_LEFT    = 1;
    private const int WMSZ_RIGHT   = 2;
    private const int WMSZ_TOP     = 3;
    private const int WMSZ_BOTTOM  = 6;

    // Retreives pointer to WindowProc function.
    private const int GWLP_WNDPROC = -4;

    // Delegate to set as new WindowProc callback function.
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    private WndProcDelegate wndProcDelegate;

    // Retrieves the thread identifier of the calling thread.
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    // Retrieves the name of the class to which the specified window belongs.
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    // Enumerates all nonchild windows associated with a thread by passing the handle to
    // each window, in turn, to an application-defined callback function.
    [DllImport("user32.dll")]
    private static extern bool EnumThreadWindows(uint dwThreadId, EnumWindowsProc lpEnumFunc, IntPtr lParam);
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    // Passes message information to the specified window procedure.
    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    // Retrieves the dimensions of the bounding rectangle of the specified window.
    // The dimensions are given in screen coordinates that are relative to the upper-left corner of the screen.
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hwnd, ref RECT lpRect);

    // Retrieves the coordinates of a window's client area. The client coordinates specify the upper-left
    // and lower-right corners of the client area. Because client coordinates are relative to the upper-left
    // corner of a window's client area, the coordinates of the upper-left corner are (0,0).
    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, ref RECT lpRect);

    // Changes an attribute of the specified window. The function also sets the 32-bit (long) value
    // at the specified offset into the extra window memory.
    [DllImport("user32.dll", EntryPoint = "SetWindowLong", CharSet = CharSet.Auto)]
    private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    // Changes an attribute of the specified window. The function also sets a value at the specified
    // offset in the extra window memory. 
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", CharSet = CharSet.Auto)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    // Name of the Unity window class used to find the window handle.
    private const string UNITY_WND_CLASSNAME = "UnityWndClass";

    // Window handle of Unity window.
    private IntPtr unityHWnd;

    // Pointer to old WindowProc callback function.
    private IntPtr oldWndProcPtr;

    // Pointer to our own WindowProc callback function.
    private IntPtr newWndProcPtr;

    /// <summary>
    /// WinAPI RECT definition.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    #endregion

    /// <summary>
    /// Called by Unity.
    /// </summary>
    void Start()
    {
        // Don't register WindowProc callback in Unity editor, because it would refer to the
        // Unity editor window, not the actual game window.
        if (Application.isEditor)
        {
            return;
        }

        // Register callback for then application wants to quit.
        Application.wantsToQuit += ApplicationWantsToQuit;

        // Find window handle of main Unity window.
        EnumThreadWindows(GetCurrentThreadId(), (hWnd, lParam) =>
        {
            var classText = new StringBuilder(UNITY_WND_CLASSNAME.Length + 1);
            GetClassName(hWnd, classText, classText.Capacity);

            if (classText.ToString() == UNITY_WND_CLASSNAME)
            {
                unityHWnd = hWnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);

        // Apply aspect ratio to current resolution.
        SetAspectRatio(aspectRatioWidth, aspectRatioHeight, true);

        // Save current fullscreen state.
        wasFullscreenLastFrame = Screen.fullScreen;

        // Register (replace) WindowProc callback. This gets called every time a window event is triggered,
        // such as resizing or moving the window.
        // Also save old WindowProc callback, as we will have to call it from our own new callback.
        wndProcDelegate = wndProc;
        newWndProcPtr = Marshal.GetFunctionPointerForDelegate(wndProcDelegate);
        oldWndProcPtr = SetWindowLong(unityHWnd, GWLP_WNDPROC, newWndProcPtr);

        // Initialization complete.
        started = true;
    }

    /// <summary>
    /// Sets the target aspect ratio to the given aspect ratio.
    /// </summary>
    /// <param name="newAspectWidth">New width of the aspect ratio.</param>
    /// <param name="newAspectHeight">New height of the aspect ratio.</param>
    /// <param name="apply">If true, the current window resolution is immediately adjusted to match the new
    /// aspect ratio. If false, this is only done the next time the window is resized manually.</param>
    public void SetAspectRatio(float newAspectWidth, float newAspectHeight, bool apply)
    {
        // Calculate new aspect ratio.
        aspectRatioWidth = newAspectWidth;
        aspectRatioHeight = newAspectHeight;
        aspect = aspectRatioWidth / aspectRatioHeight;

        // If requested, adjust resolution to match aspect ratio (this triggers WindowProc callback).
        if (apply)
        {
            Screen.SetResolution(Screen.width, Mathf.RoundToInt(Screen.width / aspect), Screen.fullScreen);
        }
    }

    /// <summary>
    /// WindowProc callback. An application-defined function that processes messages sent to a window. 
    /// </summary>
    /// <param name="hWnd">A handle to the window. </param>
    /// <param name="msg">The message used to identify the event.</param>
    /// <param name="wParam">Additional message information. The contents of this parameter
    /// depend on the value of the uMsg parameter. </param>
    /// <param name="lParam">Additional message information. The contents of this parameter
    /// depend on the value of the uMsg parameter. </param>
    /// <returns></returns>
    IntPtr wndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        // Check message type.
        // We are only interested in resize events, so ignore everything else.
        if (msg == WM_SIZING)
        {
            // Get window size struct.
            RECT rc = (RECT)Marshal.PtrToStructure(lParam, typeof(RECT));

            // Calculate window border width and height.
            RECT windowRect = new RECT();
            GetWindowRect(unityHWnd, ref windowRect);

            RECT clientRect = new RECT();
            GetClientRect(unityHWnd, ref clientRect);

            int borderWidth = windowRect.Right - windowRect.Left - (clientRect.Right - clientRect.Left);
            int borderHeight = windowRect.Bottom - windowRect.Top - (clientRect.Bottom - clientRect.Top);

            // Remove borders (including window title bar) before applying aspect ratio.
            rc.Right -= borderWidth;
            rc.Bottom -= borderHeight;

            // Clamp window size.
            int newWidth = Mathf.Clamp(rc.Right - rc.Left, minWidthPixel, maxWidthPixel);
            int newHeight = Mathf.Clamp(rc.Bottom - rc.Top, minHeightPixel, maxHeightPixel);

            // Resize according to aspect ratio and resize direction.
            switch (wParam.ToInt32())
            {
                case WMSZ_LEFT:
                    rc.Left = rc.Right - newWidth;
                    rc.Bottom = rc.Top + Mathf.RoundToInt(newWidth / aspect);
                    break;
                case WMSZ_RIGHT:
                    rc.Right = rc.Left + newWidth;
                    rc.Bottom = rc.Top + Mathf.RoundToInt(newWidth / aspect);
                    break;
                case WMSZ_TOP:
                    rc.Top = rc.Bottom - newHeight;
                    rc.Right = rc.Left + Mathf.RoundToInt(newHeight * aspect);
                    break;
                case WMSZ_BOTTOM:
                    rc.Bottom = rc.Top + newHeight;
                    rc.Right = rc.Left + Mathf.RoundToInt(newHeight * aspect);
                    break;
                case WMSZ_RIGHT + WMSZ_BOTTOM:
                    rc.Right = rc.Left + newWidth;
                    rc.Bottom = rc.Top + Mathf.RoundToInt(newWidth / aspect);
                    break;
                case WMSZ_RIGHT + WMSZ_TOP:
                    rc.Right = rc.Left + newWidth;
                    rc.Top = rc.Bottom - Mathf.RoundToInt(newWidth / aspect);
                    break;
                case WMSZ_LEFT + WMSZ_BOTTOM:
                    rc.Left = rc.Right - newWidth;
                    rc.Bottom = rc.Top + Mathf.RoundToInt(newWidth / aspect);
                    break;
                case WMSZ_LEFT + WMSZ_TOP:
                    rc.Left = rc.Right - newWidth;
                    rc.Top = rc.Bottom - Mathf.RoundToInt(newWidth / aspect);
                    break;
            }

            // Save actual Unity game area resolution.
            // This does not include borders.
            setWidth = rc.Right - rc.Left;
            setHeight = rc.Bottom - rc.Top;

            // Add back borders.
            rc.Right += borderWidth;
            rc.Bottom += borderHeight;

            // Trigger resolution change event.
            resolutionChangedEvent.Invoke(setWidth, setHeight, Screen.fullScreen);

            // Write back changed window parameters.
            Marshal.StructureToPtr(rc, lParam, true);
        }

        // Call original WindowProc function.
        return CallWindowProc(oldWndProcPtr, hWnd, msg, wParam, lParam);
    }

    /// <summary>
    /// Called by Unity.
    /// </summary>
    void Update()
    {
        // Block switching to fullscreen if fullscreen is disallowed.
        if (!allowFullscreen && Screen.fullScreen)
        {
            Screen.fullScreen = false;
        }

        if (Screen.fullScreen && !wasFullscreenLastFrame)
        {
            // Switch to fullscreen detected. Set to max screen resolution while keeping the aspect ratio.
            int height;
            int width;

            // Check where black bars will have to be added. Depending on the current aspect ratio and the actual aspect
            // ratio of the monitor, there will be black bars to the left/right or top/bottom.
            // There could of course also be none at all if the aspect ratios match exactly.
            bool blackBarsLeftRight = aspect < (float) pixelWidthOfCurrentScreen / pixelHeightOfCurrentScreen;

            if (blackBarsLeftRight) { 
                height = pixelHeightOfCurrentScreen;
                width = Mathf.RoundToInt(pixelHeightOfCurrentScreen * aspect);
            }
            else
            {
                width = pixelWidthOfCurrentScreen;
                height = Mathf.RoundToInt(pixelWidthOfCurrentScreen / aspect);
            }

            Screen.SetResolution(width, height, true);
            resolutionChangedEvent.Invoke(width, height, true);
        }
        else if (!Screen.fullScreen && wasFullscreenLastFrame)
        {
            // Switch from fullscreen to window detected. Set previous window resolution.
            Screen.SetResolution(setWidth, setHeight, false);
            resolutionChangedEvent.Invoke(setWidth, setHeight, false);
        }
        else if (!Screen.fullScreen && setWidth != -1 && setHeight != -1 && (Screen.width != setWidth || Screen.height != setHeight))
        {
            // Aero Snap detected. Set width by height.
            // Necessary because Aero Snap doesn't trigger WM_SIZING.
            setHeight = Screen.height;
            setWidth = Mathf.RoundToInt(Screen.height * aspect);

            Screen.SetResolution(setWidth, setHeight, Screen.fullScreen);
            resolutionChangedEvent.Invoke(setWidth, setHeight, Screen.fullScreen);
        }
        else if (!Screen.fullScreen)
        {
            // Save resolution of the current screen.
            // This resolution will be set as window resolution when switching to fullscreen next time.
            // Only height if needed, width will be set according to height and aspect ratio to make sure the aspect ratio
            // is kept in fullscreen mode as well.
            pixelHeightOfCurrentScreen = Screen.currentResolution.height;
            pixelWidthOfCurrentScreen = Screen.currentResolution.width;
        }

        // Save fullscreen state for next frame.
        wasFullscreenLastFrame = Screen.fullScreen;

        // Trigger resolution changed event in the editor when the game window is resized.
        #if UNITY_EDITOR
        if (Screen.width != setWidth || Screen.height != setHeight)
        {
            setWidth = Screen.width;
            setHeight = Screen.height;
            resolutionChangedEvent.Invoke(setWidth, setHeight, Screen.fullScreen);           
        }
        #endif
    }

    /// <summary>
    /// Calls SetWindowLong32 or SetWindowLongPtr64, depending on whether the executable is 32 or 64 bit.
    /// With this, we can build both 32 and 64 bit executables without running into problems.
    /// </summary>
    /// <param name="hWnd">The window handle.</param>
    /// <param name="nIndex">The zero-based offset to the value to be set.</param>
    /// <param name="dwNewLong">The replacement value.</param>
    /// <returns>If the function succeeds, the return value is the previous value of the specified offset. Otherwise zero.</returns>
    private static IntPtr SetWindowLong(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        // Check how long the IntPtr is. 4 byte means we're on 32 bit, so call functions accordingly.
        if (IntPtr.Size == 4)
        {
            return SetWindowLong32(hWnd, nIndex, dwNewLong);
        }
        return SetWindowLongPtr64(hWnd, nIndex, dwNewLong);
    }

    /// <summary>
    /// Called by Unity once application wants to quit.
    /// Returning false will abort and keep application alive. True will allow it to quit.
    /// </summary>
    private bool ApplicationWantsToQuit()
    {
        // Only allow to quit once application is initialized.
        if (!started)
            return false;

        // Delay quitting so we can clean up.
        if (!quitStarted)
        {
            StartCoroutine("DelayedQuit");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Restores old WindowProc callback, then quits.
    /// </summary>
    IEnumerator DelayedQuit()
    {
        // Re-set old WindowProc callback. Normally, this would be done in the new callback itself
        // once WM_CLOSE is detected. This seems to work fine on 64 bit, but when I build 32 bit
        // executables, this causes the application to crash on quitting.
        // This shouldn't really happen and I'm not sure why it does.
        // However, this solution right here seems to work fine on both 32 and 64 bit.
        SetWindowLong(unityHWnd, GWLP_WNDPROC, oldWndProcPtr);

        // Wait for end of frame (our callback is now un-registered), then allow application to quit.
        yield return new WaitForEndOfFrame();

        quitStarted = true;
        Application.Quit();
    }
}
