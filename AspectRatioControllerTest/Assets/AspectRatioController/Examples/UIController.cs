using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Some code for UI to interact with the ResolutionController.
/// </summary>
public class UIController : MonoBehaviour
{
    // Aspect width/height sliders.
    public Slider AspectWidthSlider;
    public Slider AspectHeightSlider;

    // Text fields showing current aspect and resolution.
    public TextMeshProUGUI AspectText;
    public TextMeshProUGUI ResolutionText;

    // The aspect ratio controller.
    public AspectRatioController AspectController;

    /// <summary>
    /// Called when aspect sliders are moved.
    /// </summary>
    /// <param name="apply">True once slider drag is finished, false otherwise.</param>
    public void AspectRatioSlidersChanged(bool apply)
    {
        int aspectRatioWidth = (int) AspectWidthSlider.value;
        int aspectRatioHeight = (int) AspectHeightSlider.value;

        AspectText.text = "Aspect: <b>" + aspectRatioWidth + ":" + aspectRatioHeight + "</b>";

        // Tell AspectRatioController to enforce new aspect ratio.
        AspectController.SetAspectRatio(aspectRatioWidth, aspectRatioHeight, apply);
    }

    /// <summary>
    /// Called when fullscreen button is clicked.
    /// </summary>
    public void ToggleFullscreen()
    {
        Screen.fullScreen = !Screen.fullScreen;
    }

    /// <summary>
    /// Callback for event from AspectRatioController that gets triggered every time
    /// the resolution of the window is changed. 
    /// </summary>
    /// <param name="width">The new window width in pixel.</param>
    /// <param name="height">The new window height in pixel.</param>
    /// <param name="fullscren">The new fullscreen state. True means fullscreen.</param>
    public void ResolutionChanged(int width, int height, bool fullscren)
    {
        ResolutionText.text = "Resolution: <b>" + width + "x" + height + "</b>";
    }
}
