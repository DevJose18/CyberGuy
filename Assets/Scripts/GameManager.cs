using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Referencias")]
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private GameObject gameOverUI; // Panel de Game Over (Canvas)

    [Header("Estado")]
    [SerializeField] private bool isGameOver = false;

    public bool IsGameOver => isGameOver;

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
    }

    private void Start()
    {
        // Si no se asignó en el Inspector, busca el PlayerHealth en la escena
        if (playerHealth == null)
        {
            playerHealth = FindFirstObjectByType<PlayerHealth>();
        }

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
    }

    private void OnDestroy()
    {
        // Limpieza de suscripciones para evitar fugas de memoria
        if (playerHealth != null)
        {
            playerHealth.OnPlayerDeath.RemoveListener(TriggerGameOver);
        }
    }

    /// <summary>
    /// Detiene el juego por completo congelando el motor de físicas y updates basados en delta time.
    /// </summary>
    public void TriggerGameOver()
    {
        if (isGameOver) return;

        isGameOver = true;
        Debug.Log("[GameManager] ¡Game Over! Deteniendo el juego.");

        UnityEditor.EditorApplication.isPlaying = false;

        // 2. Activar interfaz de Game Over
        if (gameOverUI != null)
        {
            gameOverUI.SetActive(true);
        }

        // Opcional: Desbloquear el cursor si es primera persona/VR o juego con mouse lock
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    /// <summary>
    /// Reinicia la escena actual restableciendo previamente la escala de tiempo.
    /// </summary>
    public void RestartGame()
    {
        Time.timeScale = 1.0f;
        Scene currentScene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(currentScene.buildIndex);
    }

    /// <summary>
    /// Reanuda el juego si decides implementar pausa manual.
    /// </summary>
    public void ResumeGame()
    {
        if (isGameOver) return;

        Time.timeScale = 1.0f;
    }
}