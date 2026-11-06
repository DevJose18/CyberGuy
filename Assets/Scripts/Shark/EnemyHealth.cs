using System.Collections;
using UnityEngine;

public class EnemyHealth : MonoBehaviour
{
    [Header("Estadísticas de Vida")]
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private float currentHealth;

    [Header("Feedback Visual & Hit Flash")]
    [SerializeField] private Renderer targetRenderer;
    [SerializeField] private string hitFlashColorProperty = "_HitColor";
    [SerializeField] private float hitFlashDuration = 0.12f;

    [Header("Componentes")]
    [SerializeField] private Animator animator;
    [SerializeField] private Collider enemyCollider;
    [SerializeField] private Rigidbody enemyRigidbody;
    [SerializeField] private FlyingEnemyDetector enemyDetector;
    [SerializeField] private EnemyAudio enemyAudio;

    [Header("Efectos al Morir")]
    [SerializeField] private GameObject deathVfxPrefab;
    [SerializeField] private float destroyDelay = 2.5f;

    private MaterialPropertyBlock _propBlock;
    private int _hitColorPropID;
    private bool _isDead = false;

    public bool IsDead => _isDead;

    private void Awake()
    {
        currentHealth = maxHealth;

        if (animator == null) animator = GetComponent<Animator>();
        if (enemyCollider == null) enemyCollider = GetComponent<Collider>();
        if (enemyRigidbody == null) enemyRigidbody = GetComponent<Rigidbody>();
        if (enemyDetector == null) enemyDetector = GetComponent<FlyingEnemyDetector>();
        if (enemyAudio == null) enemyAudio = GetComponent<EnemyAudio>();
        if (targetRenderer == null) targetRenderer = GetComponentInChildren<Renderer>();

        _propBlock = new MaterialPropertyBlock();
        _hitColorPropID = Shader.PropertyToID(hitFlashColorProperty);
    }

    public void TakeHitFromGun(GameObject shooter, float damage, Vector3 hitPoint, Vector3 hitNormal)
    {
        if (_isDead) return;

        if (!shooter.CompareTag("Gun"))
        {
            Debug.LogWarning($"[EnemyHealth] Impacto ignorado: '{shooter.name}' no tiene el Tag 'Gun'.");
            return;
        }

        TakeDamage(damage);

        if (targetRenderer != null)
            StartCoroutine(FlashRoutine());

        if (enemyDetector != null && !_isDead)
            enemyDetector.AlertAndAggro(shooter.transform.root);
    }

    public void TakeDamage(float amount)
    {
        if (_isDead) return;

        currentHealth -= amount;
        currentHealth = Mathf.Max(0f, currentHealth);

        // Notifica impacto al sistema de audio
        if (enemyAudio != null)
            enemyAudio.OnHitReceived();

        if (animator != null)
            animator.SetTrigger("Hit");

        if (currentHealth <= 0f)
            Die();
    }

    private void Die()
    {
        _isDead = true;

        // Notifica el inicio de la muerte: EnemyAudio maneja el sonido de agonía y el posterior de fin
        if (enemyAudio != null)
            enemyAudio.OnDeathStarted();

        if (enemyDetector != null) enemyDetector.enabled = false;
        if (enemyCollider != null) enemyCollider.enabled = false;

        if (enemyRigidbody != null)
        {
            enemyRigidbody.isKinematic = false;
            enemyRigidbody.useGravity = true;
            enemyRigidbody.linearVelocity = Vector3.down * 3f;
        }

        if (animator != null)
            animator.SetTrigger("Die");

        if (deathVfxPrefab != null)
            Instantiate(deathVfxPrefab, transform.position, Quaternion.identity);

        Destroy(gameObject, destroyDelay);
    }

    private IEnumerator FlashRoutine()
    {
        targetRenderer.GetPropertyBlock(_propBlock);
        _propBlock.SetColor(_hitColorPropID, Color.white);
        targetRenderer.SetPropertyBlock(_propBlock);

        yield return new WaitForSeconds(hitFlashDuration);

        targetRenderer.GetPropertyBlock(_propBlock);
        _propBlock.SetColor(_hitColorPropID, Color.clear);
        targetRenderer.SetPropertyBlock(_propBlock);
    }
}