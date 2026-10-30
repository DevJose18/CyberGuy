using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class FlyingEnemyDetector : MonoBehaviour
{
    [Header("Ruta Predeterminada (Spline)")]
    [SerializeField] private SplineContainer splineContainer;
    [Tooltip("Velocidad de iteración de la guía sobre el spline (m/s)")]
    [SerializeField] private float splineIterationSpeed = 4.0f;
    [SerializeField] private float hoverFrequency = 2.0f;
    [SerializeField] private float hoverAmplitude = 0.3f;

    [Header("Detección (OverlapSphere)")]
    [SerializeField] private float detectionRadius = 8.0f;
    [SerializeField] private LayerMask targetLayer;

    [Header("Persecución")]
    [SerializeField] private float chaseSpeed = 6.5f;
    [SerializeField] private float stoppingDistance = 2.0f;
    [SerializeField] private float rotationSpeed = 6.0f;

    [Header("Evasión Inteligente de Obstáculos")]
    [SerializeField] private LayerMask obstacleLayer;
    [SerializeField] private float obstacleAvoidDistance = 3.5f; // Qué tan adelante mira para esquivar
    [SerializeField] private float avoidanceForce = 2.0f;        // Intensidad con la que esquiva
    [SerializeField] private float sensorSideOffset = 0.6f;      // Separación de los sensores laterales

    
    [Header("Animaciones")]
    [SerializeField] private Animator animator;
    
    [Header("Shader / Render")]
    [SerializeField] private Renderer targetRenderer;
    [SerializeField] private string shaderBoolProperty = "_IsChasing";

    private Rigidbody _rb;
    private MaterialPropertyBlock _propBlock;
    private int _shaderPropID;
    private bool _isChasing = false;

    private float _splineProgress = 0f;
    private float _splineLength = 1f;
    private float _hoverTimer = 0f;

    private readonly Collider[] _hitColliders = new Collider[4];

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.isKinematic = true;
        _rb.useGravity = false;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        
        animator = GetComponent<Animator>();

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
            SetChasingState(true);
            ChaseTarget(_hitColliders[0].transform);
        }
        else
        {
            SetChasingState(false);
            FollowSplineTarget();
        }
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

        Vector3 desiredDirection = (destination - transform.position).normalized;
        if (desiredDirection == Vector3.zero) desiredDirection = (Vector3)tangent;

        // Modificar dirección para no estrellarse
        Vector3 moveDirection = CalculateAvoidance(desiredDirection);

        Vector3 nextPosition = transform.position + moveDirection * (currentFlightSpeed * Time.deltaTime);
        _rb.MovePosition(nextPosition);

        if (moveDirection.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(moveDirection, (Vector3)upVector);
            _rb.MoveRotation(Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime));
        }
    }

    private void ChaseTarget(Transform target)
    {
        Vector3 targetPos = target.position;
        float distance = Vector3.Distance(transform.position, targetPos);

        Vector3 desiredDirection = (targetPos - transform.position).normalized;

        // Modificar dirección para esquivar columnas/paredes camino al jugador
        Vector3 moveDirection = CalculateAvoidance(desiredDirection);

        if (moveDirection != Vector3.zero)
        {
            Quaternion targetRot = Quaternion.LookRotation(moveDirection);
            _rb.MoveRotation(Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime));
        }

        if (distance > stoppingDistance)
        {
            Vector3 nextPosition = transform.position + moveDirection * (chaseSpeed * Time.deltaTime);
            _rb.MovePosition(nextPosition);
        }
    }

    /// <summary>
    /// Lanza sensores frontales y laterales para redirigir el vuelo suavemente si hay obstáculos.
    /// </summary>
    private Vector3 CalculateAvoidance(Vector3 originalDirection)
    {
        Vector3 avoidance = Vector3.zero;

        // Raycast Central
        if (Physics.Raycast(transform.position, transform.forward, out RaycastHit hitCenter, obstacleAvoidDistance, obstacleLayer))
        {
            avoidance += hitCenter.normal * avoidanceForce;
        }

        // Sensor Derecho
        Vector3 rightOrigin = transform.position + transform.right * sensorSideOffset;
        if (Physics.Raycast(rightOrigin, transform.forward, out RaycastHit hitRight, obstacleAvoidDistance, obstacleLayer))
        {
            avoidance += -transform.right * avoidanceForce;
        }

        // Sensor Izquierdo
        Vector3 leftOrigin = transform.position - transform.right * sensorSideOffset;
        if (Physics.Raycast(leftOrigin, transform.forward, out RaycastHit hitLeft, obstacleAvoidDistance, obstacleLayer))
        {
            avoidance += transform.right * avoidanceForce;
        }

        // Sensor Superior (para evitar techos)
        Vector3 upOrigin = transform.position + transform.up * sensorSideOffset;
        if (Physics.Raycast(upOrigin, transform.forward, out RaycastHit hitUp, obstacleAvoidDistance, obstacleLayer))
        {
            avoidance += -transform.up * avoidanceForce;
        }

        // Sensor Inferior (para evitar suelo o salientes)
        Vector3 downOrigin = transform.position - transform.up * sensorSideOffset;
        if (Physics.Raycast(downOrigin, transform.forward, out RaycastHit hitDown, obstacleAvoidDistance, obstacleLayer))
        {
            avoidance += transform.up * avoidanceForce;
        }

        // Mezclar la dirección deseada con la fuerza de evasión
        return (originalDirection + avoidance).normalized;
    }

    private void SetChasingState(bool chasing)
    {
        if (_isChasing == chasing) return;
        _isChasing = chasing;
        
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

        // Visualizar los 5 rayos de evasión en la vista Scene
        Gizmos.color = Color.yellow;
        Gizmos.DrawRay(transform.position, transform.forward * obstacleAvoidDistance);
        Gizmos.DrawRay(transform.position + transform.right * sensorSideOffset, transform.forward * obstacleAvoidDistance);
        Gizmos.DrawRay(transform.position - transform.right * sensorSideOffset, transform.forward * obstacleAvoidDistance);
        Gizmos.DrawRay(transform.position + transform.up * sensorSideOffset, transform.forward * obstacleAvoidDistance);
        Gizmos.DrawRay(transform.position - transform.up * sensorSideOffset, transform.forward * obstacleAvoidDistance);
    }
}