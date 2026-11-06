using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Splines;
using Unity.Mathematics;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class FlyingEnemyDetector : MonoBehaviour
{
    private enum NavigationMode { SplineFlight, NavMeshPath }

    [Header("Ruta Predeterminada (Spline)")]
    [SerializeField] private SplineContainer splineContainer;
    [Tooltip("Velocidad de iteración de la guía sobre el spline (m/s)")]
    [SerializeField] private float splineIterationSpeed = 4.0f;
    [SerializeField] private float hoverFrequency = 2.0f;
    [SerializeField] private float hoverAmplitude = 0.3f;

    [Header("Detección (OverlapSphere)")]
    [SerializeField] private float detectionRadius = 8.0f;
    [SerializeField] private LayerMask targetLayer;

    [Header("Persecución & NavMesh")]
    [SerializeField] private float chaseSpeed = 6.5f;
    [SerializeField] private float stoppingDistance = 2.0f;
    [SerializeField] private float rotationSpeed = 6.0f;
    [SerializeField] private float flightAltitude = 3.0f;
    [SerializeField] private float cornerTolerance = 0.5f;

    [Header("Sistema de Ataque")]
    [SerializeField] private float attackDamage = 15.0f;
    [SerializeField] private float attackCooldown = 1.2f;
    [SerializeField] private float attackRange = 2.2f;        // Ligeramente superior a stoppingDistance
    [SerializeField] private float knockbackForce = 4.0f;

    [Header("Comportamiento de Pérdida / Alerta")]
    [SerializeField] private float chaseLostTimeout = 4.0f;

    [Header("Animaciones y Audio")]
    [SerializeField] private Animator animator;
    [SerializeField] private EnemyAudio enemyAudio;

    [Header("Shader / Render")]
    [SerializeField] private Renderer targetRenderer;
    [SerializeField] private string shaderBoolProperty = "_IsChasing";

    private Rigidbody _rb;
    private MaterialPropertyBlock _propBlock;
    private int _shaderPropID;
    private bool _isChasing = false;

    // Spline
    private float _splineProgress = 0f;
    private float _splineLength = 1f;
    private float _hoverTimer = 0f;

    // NavMesh y Persecución
    private NavMeshPath _navMeshPath;
    private NavigationMode _currentMode = NavigationMode.SplineFlight;
    private Transform _currentTarget;
    private float _lostTargetTimer = 0f;
    private float _attackTimer = 0f;

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

        _navMeshPath = new NavMeshPath();
        _propBlock = new MaterialPropertyBlock();
        _shaderPropID = Shader.PropertyToID(shaderBoolProperty);

        if (splineContainer != null)
            _splineLength = Mathf.Max(0.001f, splineContainer.CalculateLength());
    }

    private void Update()
    {
        // Enfriamiento de ataque
        if (_attackTimer > 0f)
            _attackTimer -= Time.deltaTime;

        // Escaneo de área
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

        // Lógica de estado
        if (_currentTarget != null)
        {
            _currentMode = NavigationMode.NavMeshPath;
            SetChasingState(true);
            ChaseAndAttackTarget(_currentTarget);
        }
        else
        {
            _currentMode = NavigationMode.SplineFlight;
            SetChasingState(false);
            FollowSplineTarget();
        }
    }

    public void AlertAndAggro(Transform attackerTransform)
    {
        _currentTarget = attackerTransform;
        _lostTargetTimer = 0f;
    }

    private void ChaseAndAttackTarget(Transform target)
    {
        Vector3 targetPos = target.position;
        float directDistance = Vector3.Distance(transform.position, targetPos);

        // Mirar siempre al objetivo mientras lo acecha
        Vector3 lookDir = (targetPos - transform.position).normalized;
        if (lookDir != Vector3.zero)
        {
            Quaternion targetRot = Quaternion.LookRotation(lookDir);
            _rb.MoveRotation(Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime));
        }

        // ¿Está en rango para asestar un golpe?
        if (directDistance <= attackRange)
        {
            PerformAttack(target);
            return; // Se detiene y ataca sin empujar ni colisionar de más
        }

        // Navegación por NavMesh mientras se acerca
        ChaseViaNavMesh(targetPos);
    }

    private void PerformAttack(Transform target)
    {
        if (_attackTimer > 0f) return;

        _attackTimer = attackCooldown;

        // 1. Animación y Sonido
        if (animator != null)
            animator.SetTrigger("Attack");

        if (enemyAudio != null)
            enemyAudio.OnAttackPerformed();

        // 2. Aplicar daño al PlayerHealth
        PlayerHealth playerHealth = target.GetComponentInParent<PlayerHealth>();
        if (playerHealth != null)
        {
            Vector3 pushDirection = (target.position - transform.position).normalized;
            playerHealth.TakeDamage(attackDamage, pushDirection, knockbackForce);
        }
    }

    private void ChaseViaNavMesh(Vector3 targetPos)
    {
        Vector3 groundOrigin = transform.position;
        Vector3 groundTarget = targetPos;

        if (NavMesh.SamplePosition(transform.position, out NavMeshHit startHit, 10f, NavMesh.AllAreas))
            groundOrigin = startHit.position;

        if (NavMesh.SamplePosition(targetPos, out NavMeshHit endHit, 10f, NavMesh.AllAreas))
            groundTarget = endHit.position;

        if (NavMesh.CalculatePath(groundOrigin, groundTarget, NavMesh.AllAreas, _navMeshPath))
        {
            if (_navMeshPath.corners != null && _navMeshPath.corners.Length > 1)
            {
                Vector3 nextWaypoint = _navMeshPath.corners[1];
                float desiredY = Mathf.Max(nextWaypoint.y + flightAltitude, targetPos.y);
                Vector3 aerialDestination = new Vector3(nextWaypoint.x, desiredY, nextWaypoint.z);

                if (Vector3.Distance(new Vector3(transform.position.x, 0, transform.position.z),
                                     new Vector3(nextWaypoint.x, 0, nextWaypoint.z)) < cornerTolerance)
                {
                    if (_navMeshPath.corners.Length > 2)
                    {
                        nextWaypoint = _navMeshPath.corners[2];
                        aerialDestination = new Vector3(nextWaypoint.x, desiredY, nextWaypoint.z);
                    }
                }

                Vector3 nextPosition = Vector3.MoveTowards(transform.position, aerialDestination, chaseSpeed * Time.deltaTime);
                _rb.MovePosition(nextPosition);
                return;
            }
        }

        // Si falla la proyección en el NavMesh, movimiento directo
        Vector3 directNext = Vector3.MoveTowards(transform.position, targetPos, chaseSpeed * Time.deltaTime);
        _rb.MovePosition(directNext);
    }

    private void FollowSplineTarget()
    {
        if (splineContainer == null) return;

        _splineProgress += (splineIterationSpeed / _splineLength) * Time.deltaTime;
        if (_splineProgress > 1f) _splineProgress -= 1f;

        splineContainer.Evaluate(_splineProgress, out float3 targetSplinePos, out float3 tangent, out float3 upVector);

        _hoverTimer += Time.deltaTime * hoverFrequency;
        Vector3 destination = (Vector3)targetSplinePos + Vector3.up * (Mathf.Sin(_hoverTimer) * hoverAmplitude);

        float distanceToGuide = Vector3.Distance(transform.position, destination);
        float currentFlightSpeed = splineIterationSpeed + (distanceToGuide * 1.5f);

        Vector3 moveDirection = (destination - transform.position).normalized;
        if (moveDirection == Vector3.zero) moveDirection = (Vector3)tangent;

        Vector3 nextPosition = Vector3.MoveTowards(transform.position, destination, currentFlightSpeed * Time.deltaTime);
        _rb.MovePosition(nextPosition);

        if (moveDirection.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(moveDirection, (Vector3)upVector);
            _rb.MoveRotation(Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime));
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

    private void OnCollisionStay(Collision collision)
    {
        // Respaldo: si el jugador choca físicamente contra el enemigo, también puede recibir daño
        if (((1 << collision.gameObject.layer) & targetLayer) != 0)
        {
            PerformAttack(collision.transform);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = _isChasing ? Color.red : new Color(0f, 1f, 1f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, detectionRadius);

        // Radio de ataque en color magenta
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}