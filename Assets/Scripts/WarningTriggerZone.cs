using System.Collections;
using System.Collections.Generic;
using Autohand;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(BoxCollider))]
public class WarningTriggerZone : MonoBehaviour
{
    [Header("Configuración de Transición")]
    [SerializeField] private float fadeDuration = 1.5f;
    [SerializeField] private float textFadeDelay = 0.4f;
    [SerializeField] private float textFadeDuration = 0.8f;
    [SerializeField] private float fadeOutDuration = 1.0f;
    [SerializeField] private bool useUnscaledTime = true;

    [Header("Daño Continuo")]
    [SerializeField] private float damagePerTick = 1f;
    [SerializeField] private float initialDamageDelay = 1.0f;
    [SerializeField] private float damageInterval = 1.5f;

    [Header("Referencias UI")]
    [SerializeField] private Canvas warningCanvas;
    [SerializeField] private Image fadeImage;
    [SerializeField] private TextMeshProUGUI warningText;

    [Header("Texto de Advertencia")]
    [SerializeField] private string warningMessage = "Warning";
    [SerializeField] private TMP_FontAsset warningFont;
    [SerializeField] private Color fadeColor = Color.black;
    [SerializeField] private Color warningTextColor = Color.red;
    [SerializeField] private float fontSize = 72f;
    [SerializeField] private float uiDistanceFromCamera = 0.35f;

    private BoxCollider _triggerCollider;
    private readonly HashSet<Collider> _playerCollidersInside = new HashSet<Collider>();
    private Coroutine _transitionCoroutine;
    private Coroutine _damageCoroutine;
    private PlayerHealth _playerHealth;
    private bool _overlayConfigured = false;
    private float _currentFadeAlpha = 0f;
    private float _currentTextAlpha = 0f;

    private void Reset()
    {
        EnsureTriggerCollider();
    }

    private void Awake()
    {
        EnsureTriggerCollider();

        if (warningCanvas != null)
        {
            warningCanvas.gameObject.SetActive(false);
        }
    }

    private void EnsureTriggerCollider()
    {
        if (_triggerCollider == null)
            _triggerCollider = GetComponent<BoxCollider>();

        if (_triggerCollider != null)
            _triggerCollider.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsPlayerCollider(other))
            return;

        bool wasEmpty = _playerCollidersInside.Count == 0;
        _playerCollidersInside.Add(other);

        if (!wasEmpty)
            return;

        _playerHealth = ResolvePlayerHealth(other);

        Camera targetCamera = ResolvePlayerCamera(other);
        if (targetCamera == null)
        {
            Debug.LogWarning("[WarningTriggerZone] No se encontró la cámara del jugador.");
            return;
        }

        EnsureWarningOverlay(targetCamera);

        if (_transitionCoroutine != null)
            StopCoroutine(_transitionCoroutine);

        _transitionCoroutine = StartCoroutine(FadeInTransition());

        if (_damageCoroutine != null)
            StopCoroutine(_damageCoroutine);

        _damageCoroutine = StartCoroutine(ApplyPeriodicDamage());
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsPlayerCollider(other))
            return;

        _playerCollidersInside.Remove(other);
        _playerCollidersInside.RemoveWhere(c => c == null || !c.enabled || !c.gameObject.activeInHierarchy);

        if (_playerCollidersInside.Count > 0)
            return;

        if (_damageCoroutine != null)
        {
            StopCoroutine(_damageCoroutine);
            _damageCoroutine = null;
        }

        if (warningCanvas == null || !warningCanvas.gameObject.activeSelf)
            return;

        if (_transitionCoroutine != null)
            StopCoroutine(_transitionCoroutine);

        _transitionCoroutine = StartCoroutine(FadeOutTransition());
    }

    private bool IsPlayerCollider(Collider other)
    {
        if (other.CompareTag("Player"))
            return true;

        if (other.GetComponentInParent<AutoHandPlayer>() != null)
            return true;

        if (other.GetComponentInParent<PlayerHealth>() != null)
            return true;

        return false;
    }

    private PlayerHealth ResolvePlayerHealth(Collider other)
    {
        PlayerHealth health = other.GetComponentInParent<PlayerHealth>();
        if (health == null)
            health = other.transform.root.GetComponentInChildren<PlayerHealth>();
        if (health == null)
            health = FindFirstObjectByType<PlayerHealth>();
        return health;
    }

    private Camera ResolvePlayerCamera(Collider other)
    {
        AutoHandPlayer player = other.GetComponentInParent<AutoHandPlayer>();
        if (player == null)
            player = AutoHandPlayer.Instance;

        if (player != null && player.headCamera != null)
            return player.headCamera;

        return Camera.main;
    }

    private void EnsureWarningOverlay(Camera targetCamera)
    {
        if (warningCanvas != null)
        {
            if (!_overlayConfigured)
            {
                warningCanvas.transform.SetParent(targetCamera.transform, false);
                warningCanvas.transform.localPosition = new Vector3(0f, 0f, uiDistanceFromCamera);
                warningCanvas.transform.localRotation = Quaternion.identity;
                warningCanvas.transform.localScale = Vector3.one * 0.001f;
                warningCanvas.renderMode = RenderMode.WorldSpace;
                warningCanvas.worldCamera = targetCamera;
                warningCanvas.sortingOrder = 1000;

                if (fadeImage == null)
                    fadeImage = warningCanvas.GetComponentInChildren<Image>(true);
                if (warningText == null)
                    warningText = warningCanvas.GetComponentInChildren<TextMeshProUGUI>(true);

                if (fadeImage != null)
                    fadeImage.raycastTarget = false;

                if (warningText != null)
                {
                    warningText.raycastTarget = false;
                    if (!string.IsNullOrEmpty(warningMessage))
                        warningText.text = warningMessage;
                    if (warningFont != null)
                        warningText.font = warningFont;
                }

                ApplyAlphas(_currentFadeAlpha, _currentTextAlpha);
                _overlayConfigured = true;
            }

            warningCanvas.gameObject.SetActive(true);
            return;
        }

        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer < 0) uiLayer = 0;

        GameObject canvasObj = new GameObject("WarningCanvas");
        canvasObj.layer = uiLayer;
        canvasObj.transform.SetParent(targetCamera.transform, false);
        canvasObj.transform.localPosition = new Vector3(0f, 0f, uiDistanceFromCamera);
        canvasObj.transform.localRotation = Quaternion.identity;
        canvasObj.transform.localScale = Vector3.one * 0.001f;

        warningCanvas = canvasObj.AddComponent<Canvas>();
        warningCanvas.renderMode = RenderMode.WorldSpace;
        warningCanvas.worldCamera = targetCamera;
        warningCanvas.sortingOrder = 1000;

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.dynamicPixelsPerUnit = 10f;

        RectTransform canvasRect = canvasObj.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(1200f, 800f);

        GameObject fadeObj = new GameObject("BlackFadeImage");
        fadeObj.layer = uiLayer;
        fadeObj.transform.SetParent(canvasObj.transform, false);

        RectTransform fadeRect = fadeObj.AddComponent<RectTransform>();
        fadeRect.anchorMin = new Vector2(0.5f, 0.5f);
        fadeRect.anchorMax = new Vector2(0.5f, 0.5f);
        fadeRect.anchoredPosition = Vector2.zero;
        fadeRect.sizeDelta = new Vector2(6000f, 6000f);

        fadeImage = fadeObj.AddComponent<Image>();
        fadeImage.raycastTarget = false;

        GameObject textObj = new GameObject("WarningText");
        textObj.layer = uiLayer;
        textObj.transform.SetParent(canvasObj.transform, false);

        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0.5f, 0.5f);
        textRect.anchorMax = new Vector2(0.5f, 0.5f);
        textRect.anchoredPosition = Vector2.zero;
        textRect.sizeDelta = new Vector2(1000f, 300f);

        warningText = textObj.AddComponent<TextMeshProUGUI>();
        warningText.raycastTarget = false;
        warningText.text = warningMessage;
        warningText.fontSize = fontSize;
        warningText.alignment = TextAlignmentOptions.Center;
        warningText.fontStyle = FontStyles.Bold;

        if (warningFont == null)
        {
            TMP_FontAsset[] loadedFonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            foreach (TMP_FontAsset f in loadedFonts)
            {
                if (f != null && f.name.IndexOf("Cyberfall", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    warningFont = f;
                    break;
                }
            }
        }

        if (warningFont != null)
        {
            warningText.font = warningFont;
        }

        ApplyAlphas(_currentFadeAlpha, _currentTextAlpha);
        _overlayConfigured = true;
    }

    private void ApplyAlphas(float fadeAlpha, float textAlpha)
    {
        _currentFadeAlpha = Mathf.Clamp01(fadeAlpha);
        _currentTextAlpha = Mathf.Clamp01(textAlpha);

        if (fadeImage != null)
            fadeImage.color = new Color(fadeColor.r, fadeColor.g, fadeColor.b, _currentFadeAlpha);

        if (warningText != null)
            warningText.color = new Color(warningTextColor.r, warningTextColor.g, warningTextColor.b, _currentTextAlpha);
    }

    private IEnumerator FadeInTransition()
    {
        float startFade = _currentFadeAlpha;
        float startText = _currentTextAlpha;
        float elapsed = 0f;
        float delay = startText > 0.01f ? 0f : textFadeDelay;
        float totalDuration = Mathf.Max(fadeDuration, delay + textFadeDuration);

        while (elapsed < totalDuration)
        {
            float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            elapsed += dt;

            float fadeT = fadeDuration > 0f ? Mathf.Clamp01(elapsed / fadeDuration) : 1f;
            float fadeAlpha = Mathf.Lerp(startFade, 1f, fadeT);

            float textElapsed = Mathf.Max(0f, elapsed - delay);
            float textT = textFadeDuration > 0f ? Mathf.Clamp01(textElapsed / textFadeDuration) : (elapsed >= delay ? 1f : 0f);
            float textAlpha = Mathf.Lerp(startText, 1f, textT);

            ApplyAlphas(fadeAlpha, textAlpha);
            yield return null;
        }

        ApplyAlphas(1f, 1f);
        _transitionCoroutine = null;
    }

    private IEnumerator FadeOutTransition()
    {
        float startFade = _currentFadeAlpha;
        float startText = _currentTextAlpha;
        float elapsed = 0f;

        while (elapsed < fadeOutDuration)
        {
            float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            elapsed += dt;

            float t = fadeOutDuration > 0f ? Mathf.Clamp01(elapsed / fadeOutDuration) : 1f;
            ApplyAlphas(Mathf.Lerp(startFade, 0f, t), Mathf.Lerp(startText, 0f, t));
            yield return null;
        }

        ApplyAlphas(0f, 0f);

        if (warningCanvas != null)
            warningCanvas.gameObject.SetActive(false);

        _transitionCoroutine = null;
    }

    private IEnumerator ApplyPeriodicDamage()
    {
        if (initialDamageDelay > 0f)
            yield return new WaitForSeconds(initialDamageDelay);

        WaitForSeconds waitInterval = damageInterval > 0f ? new WaitForSeconds(damageInterval) : null;

        while (_playerCollidersInside.Count > 0 && _playerHealth != null && !_playerHealth.IsDead)
        {
            _playerHealth.TakeDamage(damagePerTick, Vector3.zero, 0f);

            if (_playerHealth.IsDead)
                break;

            if (waitInterval != null)
                yield return waitInterval;
            else
                yield return null;
        }

        _damageCoroutine = null;
    }
}
