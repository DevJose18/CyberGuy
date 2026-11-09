using System.Collections;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class EnemyAudio : MonoBehaviour
{
    [Header("AudioSource Principal")]
    [SerializeField] private AudioSource sfxSource;

    [Header("Detección")]
    [SerializeField] private AudioClip alertClip;                // Este sí varía de pitch
    [SerializeField] private AudioClip playerSpottedExtraClip;   // Suena siempre a pitch normal (1.0)

    [Header("Combate")]
    [SerializeField] private AudioClip[] hitClips;           // Varían de pitch
    
    [Header("Ataque")]
    [SerializeField] private AudioSource screamSource;
    [SerializeField] private AudioClip attackClip;
    
    [Header("Ciclo de Muerte")]
    [SerializeField] private AudioClip dyingClip;                
    [SerializeField] private AudioClip deadFinishedClip;         
    [SerializeField] private float timeUntilDeathFinished = 2.0f;

    [Header("Configuración de Volumen y Pitch")]
    [Range(0f, 1f)] [SerializeField] private float sfxVolume = 1.0f;
    [SerializeField] private float minPitch = 0.93f;
    [SerializeField] private float maxPitch = 1.07f;
    
    
    
    private void Awake()
    {
        if (sfxSource == null)
            sfxSource = GetComponent<AudioSource>();

        ConfigureAudioSource(sfxSource);

        ConfigureAudioSource(screamSource);
    }

    private void ConfigureAudioSource(AudioSource source)
    {
        source.playOnAwake = false;
        source.volume = sfxVolume;

        if (source.spatialBlend > 0)
        {
            source.minDistance = Mathf.Max(source.minDistance, 3.0f);
            source.maxDistance = Mathf.Max(source.maxDistance, 50.0f);
        }
    }

    #region Disparadores

    public void OnPlayerDetected()
    {
        // 1. Alerta normal con variación de pitch
        if (alertClip != null)
        {
            sfxSource.pitch = Random.Range(minPitch, maxPitch);
            sfxSource.PlayOneShot(alertClip, sfxVolume);
        }

        // 2. Sonido extra por su propio canal: garantiza pitch nativo de 1.0f sin interferencias
        if (playerSpottedExtraClip != null)
        {
            screamSource.PlayOneShot(playerSpottedExtraClip, sfxVolume);
        }
    }

    public void OnHitReceived()
    {
        if (hitClips == null || hitClips.Length == 0) return;

        AudioClip clip = hitClips[Random.Range(0, hitClips.Length)];
        if (clip != null)
        {
            sfxSource.pitch = Random.Range(minPitch, maxPitch);
            sfxSource.PlayOneShot(clip, sfxVolume);
        }
    }

    public void OnDeathStarted()
    {
        if (dyingClip != null)
        {
            sfxSource.pitch = Random.Range(minPitch, maxPitch);
            sfxSource.PlayOneShot(dyingClip, sfxVolume);
        }

        StartCoroutine(DeathAudioSequenceRoutine());
    }

        private IEnumerator DeathAudioSequenceRoutine()
        {
            // Guardamos la última posición válida por si el enemigo se destruye durante la espera
            Vector3 lastPosition = transform.position;
            float savedSpatialBlend = sfxSource != null ? sfxSource.spatialBlend : 1f;
            float savedMinDist = sfxSource != null ? sfxSource.minDistance : 3f;
            float savedMaxDist = sfxSource != null ? sfxSource.maxDistance : 50f;

            yield return new WaitForSeconds(timeUntilDeathFinished);

            if (deadFinishedClip != null)
            {
                GameObject tempGO = new GameObject("DeadFinishedAudio_Temp");
                tempGO.transform.position = lastPosition;

                AudioSource tempSource = tempGO.AddComponent<AudioSource>();
                tempSource.clip = deadFinishedClip;
                tempSource.volume = sfxVolume;
                tempSource.spatialBlend = savedSpatialBlend;
                tempSource.minDistance = savedMinDist;
                tempSource.maxDistance = savedMaxDist;
                tempSource.pitch = 1.0f;
                tempSource.Play();

                Destroy(tempGO, deadFinishedClip.length + 0.1f);
            }
    }

    #endregion
    
    public void OnAttackPerformed()
    {
        if (attackClip != null)
        {
            sfxSource.pitch = Random.Range(minPitch, maxPitch);
            sfxSource.PlayOneShot(attackClip, sfxVolume);
        }
    }
}