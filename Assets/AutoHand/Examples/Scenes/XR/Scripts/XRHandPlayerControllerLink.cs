using UnityEngine;

namespace Autohand.Demo {

    [HelpURL("https://app.gitbook.com/s/5zKO0EvOjzUDeT2aiFk3/auto-hand/controller-input")]
    public class XRHandPlayerControllerLink : MonoBehaviour{
        public XRHandControllerLink moveController;
        public XRHandControllerLink turnController;
        public AutoHandPlayer player;

        [Header("Input")]
        public Common2DAxis moveAxis;
        public Common2DAxis turnAxis;
        
        bool canFly => moveController.ButtonPressed(CommonButton.triggerButton) && turnController.ButtonPressed(CommonButton.triggerButton);
        [SerializeField] private AudioSource _audioSource;

        void Update()
        {
            player.Move(moveController.GetAxis2D(moveAxis));
            player.Turn(turnController.GetAxis2D(turnAxis).x);
            player.Jump(canFly);
            if (canFly && _audioSource != null && !_audioSource.isPlaying)
            {
                _audioSource.Play();
            }
        }
        void FixedUpdate(){
            player.Move(moveController.GetAxis2D(moveAxis));
        }
    }
}