using System.Collections;
using System.Collections.Generic;
using Autohand.Demo;
using UnityEngine;

    public class Pistol : MonoBehaviour
    {
        public Rigidbody body;

        public Transform barrelTip;
        [SerializeField] private float damage = 25f;
        public float hitPower = 1;
        public float recoilPower = 1;
        public float range = 100;
        public LayerMask layer;

        public AudioClip shootSound;
        public float shootVolume = 1f;

        private void Start() {
            if(body == null && GetComponent<Rigidbody>() != null)
                body = GetComponent<Rigidbody>();
        }

        public void Shoot() {
            //Play the audio sound
            if(shootSound)
                AudioSource.PlayClipAtPoint(shootSound, transform.position, shootVolume);

            RaycastHit hit;
            if(Physics.Raycast(barrelTip.position, barrelTip.forward, out hit, range, layer)){
                var hitBody = hit.transform.GetComponent<Rigidbody>();
                if(hitBody != null) {
                    Debug.DrawRay(barrelTip.position, (hit.point - barrelTip.position), Color.green, 5);
                    hitBody.GetComponent<Smash>()?.DoSmash();
                    hitBody.AddForceAtPosition((hit.point - barrelTip.position).normalized*hitPower*10, hit.point, ForceMode.Impulse);
                }
                
                EnemyHealth enemyHealth = hit.collider.GetComponentInParent<EnemyHealth>();

                if (enemyHealth != null)
                {
                    // Pasa 'this.gameObject' (que debe tener el tag "Gun")
                    enemyHealth.TakeHitFromGun(gameObject, damage, hit.point, hit.normal);
                }
            }
            else
                Debug.DrawRay(barrelTip.position, barrelTip.forward*range, Color.red, 1);

            body.AddForce(barrelTip.transform.up*recoilPower*5, ForceMode.Impulse);
        }
    }
