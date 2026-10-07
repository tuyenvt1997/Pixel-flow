using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PixelFlow.Meta
{
    /// <summary>
    /// Boot screen: loads <see cref="SceneFlow.MenuScene"/> asynchronously and fills the progress bar, holding the
    /// screen for at least <c>minimumSeconds</c> so it never just flashes by. The bar shows the smaller of the real
    /// load progress and the elapsed fraction of the minimum time.
    /// </summary>
    public sealed class LoadingScreen : MonoBehaviour
    {
        // AsyncOperation.progress stops at 0.9 while allowSceneActivation is false.
        private const float LoadedProgress = 0.9f;

        [Tooltip("Progress bar (0..1).")]
        [SerializeField] private Slider progressBar;

        [Tooltip("Minimum time the loading screen stays up, in seconds.")]
        [Min(0f)]
        [SerializeField] private float minimumSeconds = 1.5f;

        [Tooltip("Scene loaded once the bar is full.")]
        [SerializeField] private string sceneName = SceneFlow.MenuScene;

        private void Awake()
        {
            Application.targetFrameRate = 60;
        }

        private IEnumerator Start()
        {
            SetProgress(0f);
            AsyncOperation load = SceneManager.LoadSceneAsync(sceneName);
            if (load == null)
                yield break;
            load.allowSceneActivation = false;

            float elapsed = 0f;
            while (true)
            {
                elapsed += Time.unscaledDeltaTime;
                float loaded = Mathf.Clamp01(load.progress / LoadedProgress);
                float timed = minimumSeconds > 0f ? Mathf.Clamp01(elapsed / minimumSeconds) : 1f;
                SetProgress(Mathf.Min(loaded, timed));
                if (loaded >= 1f && timed >= 1f)
                    break;
                yield return null;
            }

            load.allowSceneActivation = true;
        }

        private void SetProgress(float value)
        {
            if (progressBar != null)
                progressBar.value = value;
        }
    }
}
