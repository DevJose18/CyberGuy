using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class FlyingEnemyDetector : MonoBehaviour
{
    [Header("Patrulla Circular")]
    [Tooltip("Punto central sobre el que volará en círculos. Si está vacío, usará su punto de spawn.")]
    [SerializeField] private Transform orbitCenter;
    [SerializeField] private float orbitRadius = 6.0f;
    [SerializeField] private float orbitSpeed = 40.0f;         // Grados por segundo
    [SerializeField] private float initialAngleOffset = 0.0f;  // Para repartir varios enemigos (ej: 0, 120, 240)
    [SerializeField] private bool clockwise = true;
    [SerializeField] private float hoverFrequency = 2.0f;
    [SerializeField] private float hoverAmplitude = 0.35f;

    [Header("Detección")]
    [SerializeField] private float detectionRadius = 8.0f;
    [SerializeField] private LayerMask targetLayer;            // Capa del jugador (Player / HandPlayer)

    [Header("Persecución y Ataque")]
    [SerializeField] private float chaseSpeed = 6.0f;
    [SerializeField] private float attackRange = 2.2f;
    [SerializeField] private float attackDamage = 15.0f;
    [SerializeField] private float attackCooldown = 1.2f;
    [SerializeField] private float knockbackForce = 4.0f;
    [SerializeField] private float rotationSpeed = 6.0f;
    [SerializeField] private float chaseLostTimeout = 4.0f;

    [Header("Referencias")]
    [SerializeField] private Animator animator;
    [SerializeField] private EnemyAudio enemyAudio;
    [SerializeField] private Renderer targetRenderer;
    [SerializeField] private string shaderBoolProperty = "_IsChasing";

    private Rigidbody _rb;
    private MaterialPropertyBlock _propBlock;
    private int _shaderPropID;
    private bool _isChasing = false;

    private Vector3 _spawnCenter;
    private float _currentAngle = 0f;
    private float _hoverTimer = 0f;
    private float _attackTimer = 0f;
    private float _lostTargetTimer = 0f;
    private Transform _currentTarget;

    private readonly Collider[] _hitColliders = new Collider[4];

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.isKinematic = true;
        _rb.useGravity = false;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;

        if (animator == null) animator = GetComponent<Animator>();
        if (enemyAudio == null) enemyAudio = GetComponent<EnemyAudio>();
        if (targetRenderer == null) targetRenderer = GetComponentInChildren<Renderer>();

        _propBlock = new MaterialPropertyBlock();
        _shaderPropID = Shader.PropertyToID(shaderBoolProperty);

        // Si no se asigna un centro, el centro de rotación es donde se coloca en escena
        _spawnCenter = transform.position;
        _currentAngle = initialAngleOffset;
        _hoverTimer = Random.Range(0f, 10f);
    }

    private void Update()
    {
        if (_attackTimer > 0f)
            _attackTimer -= Time.deltaTime;

        CheckDetection();

        if (_currentTarget != null)
        {
            SetChasingState(true);
            ChaseAndAttack();
        }
        else
        {
            SetChasingState(false);
            CircleFlight();
        }
    }

    private void CheckDetection()
    {
        int hits = Physics.OverlapSphereNonAlloc(transform.position, detectionRadius, _hitColliders, targetLayer);

        if (hits > 0)
        {
            _currentTarget = _hitColliders[0].transform;
            _lostTargetTimer = 0f;
        }
        else if (_currentTarget != null)
        {
            _lostTargetTimer += Time.deltaTime;
            if (_lostTargetTimer >= chaseLostTimeout)
            {
                _currentTarget = null;
                _lostTargetTimer = 0f;
            }
        }
    }

    public void AlertAndAggro(Transform attackerTransform)
    {
        _currentTarget = attackerTransform;
        _lostTargetTimer = 0f;
    }

    /// <summary>
    /// Vuelo circular trigonométrico continuo.
    /// </summary>
    private void CircleFlight()
    {
        Vector3 center = orbitCenter != null ? orbitCenter.position : _spawnCenter;

        // 1. Incrementar el ángulo según el sentido de giro
        float directionFactor = clockwise ? 1f : -1f;
        _currentAngle += directionFactor * orbitSpeed * Time.deltaTime;
        if (_currentAngle >= 360f) _currentAngle -= 360f;
        if (_currentAngle < 0f) _currentAngle += 360f;

        // 2. Coordenadas circulares en el plano horizontal (X, Z) + fluctuación vertical (Y)
        float rad = _currentAngle * Mathf.Deg2Rad;
        _hoverTimer += Time.deltaTime * hoverFrequency;
        float verticalOffset = Mathf.Sin(_hoverTimer) * hoverAmplitude;

        Vector3 targetOrbitPos = center + new Vector3(
            Mathf.Cos(rad) * orbitRadius,
            verticalOffset,
            Mathf.Sin(rad) * orbitRadius
        );

        // 3. Mover hacia la posición orbital (permite reincorporarse suavemente si venía persiguiendo al jugador)
        Vector3 nextPos = Vector3.MoveTowards(transform.position, targetOrbitPos, (orbitSpeed * Mathf.Deg2Rad * orbitRadius + 1f) * Time.deltaTime);
        _rb.MovePosition(nextPos);

        // 4. Orientar el frente en la dirección tangencial del círculo
        Vector3 moveDir = (targetOrbitPos - transform.position).normalized;
        if (moveDir == Vector3.zero)
        {
            // Vector tangente exacto a la circunferencia
            moveDir = new Vector3(-Mathf.Sin(rad) * directionFactor, 0f, Mathf.Cos(rad) * directionFactor).normalized;
        }

        if (moveDir != Vector3.zero)
        {
            Quaternion targetRot = Quaternion.LookRotation(moveDir);
            _rb.MoveRotation(Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime));
        }
    }

    private void ChaseAndAttack()
    {
        Vector3 aimPoint = _currentTarget.position + Vector3.up * 1.2f;
        float distance = Vector3.Distance(transform.position, aimPoint);

        // Orientarse hacia el jugador
        Vector3 lookDir = (aimPoint - transform.position).normalized;
        if (lookDir != Vector3.zero)
        {
            Quaternion targetRot = Quaternion.LookRotation(lookDir);
            _rb.MoveRotation(Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime));
        }

        // Rango de ataque
        if (distance <= attackRange)
        {
            PerformAttack();
            return;
        }

        // Persecución directa
        Vector3 nextPos = Vector3.MoveTowards(transform.position, aimPoint, chaseSpeed * Time.deltaTime);
        _rb.MovePosition(nextPos);
    }

    private void PerformAttack()
    {
        if (_attackTimer > 0f) return;
        _attackTimer = attackCooldown;

        if (animator != null)
            animator.SetTrigger("Attack");

        if (enemyAudio != null)
            enemyAudio.OnAttackPerformed();

        PlayerHealth health = _currentTarget.GetComponentInParent<PlayerHealth>();
        if (health == null)
            health = _currentTarget.root.GetComponentInChildren<PlayerHealth>();

        if (health != null)
        {
            Vector3 pushDirection = (_currentTarget.position - transform.position).normalized;
            health.TakeDamage(attackDamage, pushDirection, knockbackForce);
        }
    }

    private void SetChasingState(bool chasing)
    {
        if (_isChasing == chasing) return;
        _isChasing = chasing;

        if (_isChasing && enemyAudio != null)
            enemyAudio.OnPlayerDetected();

        if (animator != null)
            animator.SetBool("Is_Chasing", chasing);

        if (targetRenderer != null)
        {
            targetRenderer.GetPropertyBlock(_propBlock);
            _propBlock.SetFloat(_shaderPropID, _isChasing ? 1.0f : 0.0f);
            targetRenderer.SetPropertyBlock(_propBlock);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 center = orbitCenter != null ? orbitCenter.position : (Application.isPlaying ? _spawnCenter : transform.position);

        // Dibujar el círculo de patrulla
        Gizmos.color = Color.cyan;
        int segments = 32;
        float step = 360f / segments;
        Vector3 prevPoint = center + new Vector3(Mathf.Cos(0) * orbitRadius, 0f, Mathf.Sin(0) * orbitRadius);

        for (int i = 1; i <= segments; i++)
        {
            float rad = i * step * Mathf.Deg2Rad;
            Vector3 nextPoint = center + new Vector3(Mathf.Cos(rad) * orbitRadius, 0f, Mathf.Sin(rad) * orbitRadius);
            Gizmos.DrawLine(prevPoint, nextPoint);
            prevPoint = nextPoint;
        }

        // Radios de detección y ataque
        Gizmos.color = _isChasing ? Color.red : new Color(0f, 1f, 1f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, detectionRadius);

        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}