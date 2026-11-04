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
    [SerializeField] private float flightAltitude = 3.0f;          // Altura sobre el NavMesh
    [SerializeField] private float cornerTolerance = 0.5f;         // Distancia para pasar al siguiente nodo

    [Header("Animaciones")]
    [SerializeField] private Animator animator;

    [Header("Shader / Render")]
    [SerializeField] private Renderer targetRenderer;
    [SerializeField] private string shaderBoolProperty = "_IsChasing";

    private Rigidbody _rb;
    private MaterialPropertyBlock _propBlock;
    private int _shaderPropID;
    private bool _isChasing = false;

    // Spline variables
    private float _splineProgress = 0f;
    private float _splineLength = 1f;
    private float _hoverTimer = 0f;

    // NavMesh variables
    private NavMeshPath _navMeshPath;
    private int _currentPathIndex = 0;
    private NavigationMode _currentMode = NavigationMode.SplineFlight;

    private readonly Collider[] _hitColliders = new Collider[4];

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.isKinematic = true;
        _rb.useGravity = false;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;

        if (animator == null)
            animator = GetComponent<Animator>();

        _navMeshPath = new NavMeshPath();
        _propBlock = new MaterialPropertyBlock();
        _shaderPropID = Shader.PropertyToID(shaderBoolProperty);

        if (targetRenderer == null)
            targetRenderer = GetComponentInChildren<Renderer>();

        if (splineContainer != null)
            _splineLength = Mathf.Max(0.001f, splineContainer.CalculateLength());
    }

    private void Update()
    {
        int hits = Physics.OverlapSphereNonAlloc(transform.position, detectionRadius, _hitColliders, targetLayer);

        if (hits > 0)
        {
            // Alternar a modo NavMesh para rodear obstáculos
            _currentMode = NavigationMode.NavMeshPath;
            SetChasingState(true);
            ChaseViaNavMesh(_hitColliders[0].transform);
        }
        else
        {
            // Alternar de vuelta a la ruta predeterminada por Spline
            _currentMode = NavigationMode.SplineFlight;
            SetChasingState(false);
            FollowSplineTarget();
        }
    }

    private void FollowSplineTarget()
    {
        if (splineContainer == null) return;

        // Avanzar la guía por la curva
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

    private void ChaseViaNavMesh(Transform target)
    {
        Vector3 targetPos = target.position;
        float directDistance = Vector3.Distance(transform.position, targetPos);

        if (directDistance <= stoppingDistance) return;

        // 1. Proyectar posición actual y destino al suelo horneado con NavMesh
        Vector3 groundOrigin = transform.position;
        Vector3 groundTarget = targetPos;

        if (NavMesh.SamplePosition(transform.position, out NavMeshHit startHit, 10f, NavMesh.AllAreas))
            groundOrigin = startHit.position;

        if (NavMesh.SamplePosition(targetPos, out NavMeshHit endHit, 10f, NavMesh.AllAreas))
            groundTarget = endHit.position;

        // 2. Calcular ruta libre de muros
        if (NavMesh.CalculatePath(groundOrigin, groundTarget, NavMesh.AllAreas, _navMeshPath))
        {
            if (_navMeshPath.corners != null && _navMeshPath.corners.Length > 1)
            {
                // El corner[0] es la posición actual en el NavMesh; corner[1] es el siguiente punto de paso
                Vector3 nextWaypoint = _navMeshPath.corners[1];

                // Elevar el waypoint a la altura deseada de vuelo o a la altura relativa del jugador
                float desiredY = Mathf.Max(nextWaypoint.y + flightAltitude, targetPos.y);
                Vector3 aerialDestination = new Vector3(nextWaypoint.x, desiredY, nextWaypoint.z);

                // Si ya estamos muy cerca del nodo de giro, mirar al siguiente si existe
                if (Vector3.Distance(new Vector3(transform.position.x, 0, transform.position.z), 
                                     new Vector3(nextWaypoint.x, 0, nextWaypoint.z)) < cornerTolerance)
                {
                    if (_navMeshPath.corners.Length > 2)
                    {
                        nextWaypoint = _navMeshPath.corners[2];
                        aerialDestination = new Vector3(nextWaypoint.x, desiredY, nextWaypoint.z);
                    }
                }

                // 3. Desplazamiento y rotación a través de Rigidbody
                Vector3 moveDirection = (aerialDestination - transform.position).normalized;
                if (moveDirection != Vector3.zero)
                {
                    Quaternion targetRot = Quaternion.LookRotation(moveDirection);
                    _rb.MoveRotation(Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime));
                }

                Vector3 nextPosition = Vector3.MoveTowards(transform.position, aerialDestination, chaseSpeed * Time.deltaTime);
                _rb.MovePosition(nextPosition);
                return;
            }
        }

        // Respaldo en línea recta si no hay NavMesh disponible bajo el objetivo
        Vector3 directDir = (targetPos - transform.position).normalized;
        if (directDir != Vector3.zero)
        {
            _rb.MoveRotation(Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(directDir), rotationSpeed * Time.deltaTime));
            _rb.MovePosition(Vector3.MoveTowards(transform.position, targetPos, chaseSpeed * Time.deltaTime));
        }
    }

    private void SetChasingState(bool chasing)
    {
        if (_isChasing == chasing) return;
        _isChasing = chasing;

        if (animator != null)
            animator.SetBool("Is_Chasing", chasing);

        if (targetRenderer != null)
        {
            targetRenderer.GetPropertyBlock(_propBlock);
            _propBlock.SetFloat(_shaderPropID, _isChasing ? 1.0f : 0.0f);
            targetRenderer.SetPropertyBlock(_propBlock);
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (((1 << collision.gameObject.layer) & targetLayer) != 0)
        {
            Debug.Log($"Impacto con el jugador: {collision.gameObject.name}");
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = _isChasing ? Color.red : new Color(0f, 1f, 1f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, detectionRadius);

        // Visualizar la ruta calculada por el NavMesh en la vista de escena
        if (_navMeshPath != null && _navMeshPath.corners != null && _navMeshPath.corners.Length > 1)
        {
            Gizmos.color = Color.magenta;
            for (int i = 0; i < _navMeshPath.corners.Length - 1; i++)
            {
                Vector3 p1 = _navMeshPath.corners[i] + Vector3.up * flightAltitude;
                Vector3 p2 = _navMeshPath.corners[i + 1] + Vector3.up * flightAltitude;
                Gizmos.DrawLine(p1, p2);
                Gizmos.DrawSphere(p1, 0.2f);
            }
        }
    }
}