using System;
using System.Collections;
using Autohand;
using Autohand.Demo;
using Unity.Burst.Intrinsics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }
    public static Action<bool> OnGamePaused;

    [Header("Referencias")]
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private GameObject WonUI; // Panel de Game Over (Canvas)
    [SerializeField] private GameObject gameOverUI; // Panel de Game Over (Canvas)
    [SerializeField] private GameObject[] enemies;
    private int enemiesCount;

    [Header("Pausa y Jugador")]
    [SerializeField] private GameObject pauseCanvas;
    [SerializeField] private AutoHandPlayer autoHandPlayer;
    [SerializeField] private XRHandPlayerControllerLink playerControllerLink;
    [SerializeField] private Transform playerTransform;
    [SerializeField] private Transform headCameraTransform;

    [Header("Posicionamiento del Canvas de Pausa")]
    [SerializeField] private float pauseCanvasDistance = 2.0f;
    [SerializeField] private float pauseCanvasHeightOffset = 0.0f;
    [SerializeField] private bool keepCanvasHorizontal = true;
    [SerializeField] private bool allowKeyboardPause = true;

    [Header("Navegación")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";
    
    [Header("Estado")]
    [SerializeField] private bool isGameOver = false;
    [SerializeField] private bool didPlayerWin = false;
    [SerializeField] private bool isPaused = false;

    public bool IsGameOver => isGameOver;
    public bool IsPaused => isPaused;

    private Rigidbody _playerRigidbody;
    private RigidbodyConstraints _savedConstraints = RigidbodyConstraints.None;
    private bool _savedUseMovement = true;
    private bool _savedControllerLinkEnabled = true;
    private bool _movementFrozen = false;

    private void Awake()
    {
        // Configuración Singleton básica
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        
        // Asegurar que el tiempo esté corriendo al cargar la escena
        Time.timeScale = 1.0f;
        isPaused = false;

        enemies = GameObject.FindGameObjectsWithTag("Enemy");
    }

    private void Start()
    {
        ResolvePlayerReferences();

        // Suscribirse al evento de muerte del jugador
        if (playerHealth != null)
        {
            playerHealth.OnPlayerDeath.AddListener(TriggerGameOver);
        }
        else
        {
            Debug.LogWarning("[GameManager] No se encontró ninguna instancia de PlayerHealth en la escena.");
        }

        if (gameOverUI != null)
        {
            gameOverUI.SetActive(false);
        }

        if (WonUI != null)
        {
            WonUI.SetActive(false);
        }

        if (pauseCanvas != null)
        {
            pauseCanvas.SetActive(false);
        }
    }

    private void Update()
    {
        if (allowKeyboardPause && !isGameOver && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame || 
            playerControllerLink.moveController.ButtonPressed(CommonButton.menuButton) && !isPaused)
        {
            TogglePause();
        }
    }

    private void ResolvePlayerReferences()
    {
        // Si no se asignó en el Inspector, busca el PlayerHealth en la escena
        if (playerHealth == null)
        {
            playerHealth = FindFirstObjectByType<PlayerHealth>();
        }

        if (autoHandPlayer == null)
        {
            autoHandPlayer = FindFirstObjectByType<AutoHandPlayer>();
        }

        if (playerControllerLink == null)
        {
            playerControllerLink = FindFirstObjectByType<XRHandPlayerControllerLink>();
        }

        if (playerTransform == null)
        {
            if (autoHandPlayer != null)
                playerTransform = autoHandPlayer.transform;
            else if (playerHealth != null)
                playerTransform = playerHealth.transform;
        }

        if (headCameraTransform == null)
        {
            if (autoHandPlayer != null && autoHandPlayer.headCamera != null)
                headCameraTransform = autoHandPlayer.headCamera.transform;
            else if (Camera.main != null)
                headCameraTransform = Camera.main.transform;
        }

        if (_playerRigidbody == null)
        {
            if (autoHandPlayer != null)
                _playerRigidbody = autoHandPlayer.GetComponent<Rigidbody>();
            else if (playerTransform != null)
                _playerRigidbody = playerTransform.GetComponent<Rigidbody>();
        }
    }

    private void OnEnable()
    {
        EnemyHealth.OnEnemyDeath += OnEnemyEliminated;
    }
    

    private void OnDestroy()
    {
        // Limpieza de suscripciones para evitar fugas de memoria
        if (playerHealth != null)
        {
            playerHealth.OnPlayerDeath.RemoveListener(TriggerGameOver);
        }
        EnemyHealth.OnEnemyDeath -= OnEnemyEliminated;
    }

    /// <summary>
    /// Detiene el juego por completo congelando el motor de físicas y updates basados en delta time.
    /// </summary>
    public void TriggerGameOver()
    {
        StartCoroutine(StartGameEnded());
    }
    

    IEnumerator StartGameEnded()
    {
        if (isGameOver) yield break;
        
        isGameOver = true;
        if (isPaused)
        {
            isPaused = false;
            if (pauseCanvas != null) pauseCanvas.SetActive(false);
            SetPlayerMovementEnabled(true);
            Time.timeScale = 1.0f;
        }

        Debug.Log("[GameManager] ¡Game Over! Deteniendo el juego.");


        // 2. Activar interfaz de Game Over
        if (WonUI != null && didPlayerWin)
        {
            WonUI.SetActive(true);
        }
        else if (gameOverUI != null && !didPlayerWin)
        {
            gameOverUI.SetActive(true);
        }
        
        yield return new WaitForSeconds(3f);

        #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
        #endif
        
        // Opcional: Desbloquear el cursor si es primera persona/VR o juego con mouse lock
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    /// <summary>
    /// Alterna entre pausa y juego activo cuando se presiona el botón de pausa.
    /// </summary>
    public void TogglePause()
    {
        if (isGameOver) return;

        if (isPaused)
            ResumeGame();
        else
            PauseGame();
    }

    /// <summary>
    /// Alias para conectar directamente desde eventos del botón de pausa.
    /// </summary>
    public void OnPauseButtonPressed()
    {
        TogglePause();
    }

    /// <summary>
    /// Pausa el juego, teletransporta el Canvas de pausa frente al jugador e impide que se mueva.
    /// </summary>
    public void PauseGame()
    {
        if (isGameOver || isPaused) return;

        ResolvePlayerReferences();
        isPaused = true;

        TeleportPauseCanvasToPlayer();

        if (pauseCanvas != null)
        {
            pauseCanvas.SetActive(true);
        }

        SetPlayerMovementEnabled(false);
        //Time.timeScale = 0f;
        OnGamePaused.Invoke(true);
    }

    /// <summary>
    /// Reanuda el juego ocultando el Canvas de pausa y devolviendo el movimiento al personaje.
    /// </summary>
    public void ResumeGame()
    {
        if (isGameOver) return;

        isPaused = false;

        if (pauseCanvas != null)
        {
            pauseCanvas.SetActive(false);
        }

        SetPlayerMovementEnabled(true);
        OnGamePaused.Invoke(false);
        //Time.timeScale = 1.0f;
    }

    private void TeleportPauseCanvasToPlayer()
    {
        if (pauseCanvas == null) return;

        Transform reference = headCameraTransform != null ? headCameraTransform : playerTransform;
        if (reference == null) return;

        Vector3 forward = reference.forward;
        if (keepCanvasHorizontal)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
                forward = playerTransform != null ? playerTransform.forward : Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.forward;
            forward.Normalize();
        }

        Vector3 targetPosition = reference.position + forward * pauseCanvasDistance + Vector3.up * pauseCanvasHeightOffset;
        pauseCanvas.transform.position = targetPosition;

        Vector3 lookDirection = targetPosition - reference.position;
        if (keepCanvasHorizontal)
        {
            lookDirection.y = 0f;
        }

        if (lookDirection.sqrMagnitude > 0.0001f)
        {
            pauseCanvas.transform.rotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
        }
    }

    private void SetPlayerMovementEnabled(bool movementEnabled)
    {
        if (!movementEnabled)
        {
            if (_movementFrozen) return;
            _movementFrozen = true;

            if (autoHandPlayer != null)
            {
                _savedUseMovement = autoHandPlayer.useMovement;
                autoHandPlayer.useMovement = false;
            }

            if (playerControllerLink != null)
            {
                _savedControllerLinkEnabled = playerControllerLink.enabled;
                playerControllerLink.enabled = false;

                AudioSource jetpackAudio = playerControllerLink.GetComponent<AudioSource>();
                if (jetpackAudio != null && jetpackAudio.isPlaying)
                {
                    jetpackAudio.Stop();
                }
            }

            if (_playerRigidbody != null)
            {
                _savedConstraints = _playerRigidbody.constraints;
                if (!_playerRigidbody.isKinematic)
                {
                    _playerRigidbody.linearVelocity = Vector3.zero;
                    _playerRigidbody.angularVelocity = Vector3.zero;
                }
                _playerRigidbody.constraints = RigidbodyConstraints.FreezeAll;
            }
        }
        else
        {
            if (!_movementFrozen) return;
            _movementFrozen = false;

            if (autoHandPlayer != null)
            {
                autoHandPlayer.useMovement = _savedUseMovement;
            }

            if (playerControllerLink != null)
            {
                playerControllerLink.enabled = _savedControllerLinkEnabled;
            }

            if (_playerRigidbody != null)
            {
                _playerRigidbody.constraints = _savedConstraints;
            }
        }
    }

    /// <summary>
    /// Reinicia la escena actual restableciendo previamente la escala de tiempo.
    /// </summary>
    public void RestartGame()
    {
        isPaused = false;
        SetPlayerMovementEnabled(true);
        Time.timeScale = 1.0f;
        Scene currentScene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(currentScene.buildIndex);
    }

    /// <summary>
    /// Carga la escena del Menú Principal restableciendo la escala de tiempo.
    /// </summary>
    public void LoadMainMenu()
    {
        isPaused = false;
        SetPlayerMovementEnabled(true);
        Time.timeScale = 1.0f;
        SceneManager.LoadScene(mainMenuSceneName);
    }

    /// <summary>
    /// Cierra la aplicación (o detiene Play Mode en el Editor).
    /// </summary>
    public void QuitGame()
    {
        #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
        #else
            Application.Quit();
        #endif
    }

    private void OnEnemyEliminated()
    {
        Debug.Log("Enemy eliminado");
        enemiesCount++;
        if (enemiesCount == enemies.Length)
        {
            didPlayerWin = true;
            TriggerGameOver();
        }
    }
}