using UnityEngine;
#if GAIA_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif


namespace Gaia
{

    public class GaiaCharacterSwitcher : MonoBehaviour
    {
        private GRCControllerSupport m_grc;
        bool m_switchFlag;
        public GRC_ControllerSettings m_grcSettings;
#if GAIA_INPUT_SYSTEM
        public Key m_switchKeyCode = Key.B;
#else
        public KeyCode m_switchKeyCode;
#endif



        public void Start()
        {
            m_grc = new GRCControllerSupport();
            GRCControllerSupport.SetControllerSettings(m_grcSettings);


            GameObject gaiaPlayer = GaiaUtils.GetPlayerGameObject();
            if (gaiaPlayer.transform.Find("NestedParent_Unpack/PlayerCapsule") != null)
                m_grc.m_selectedControllerType = ControllerType.FirstPerson;
            else if (gaiaPlayer.transform.Find("NestedParent_Unpack/PlayerArmature") != null)
                m_grc.m_selectedControllerType = ControllerType.ThirdPerson;
            else
                //assume Fly Cam as default
                m_grc.m_selectedControllerType = ControllerType.FlyCam;

        }

        public void FlagCharacterSwitch()
        {
            //Remove the controller from scene, and set a flag do the actual switch on the next late update
            //this prevents race conditions with switching between the unity controller types
            m_grc.m_overrideTargetLocation = true;
            m_grc.m_targetLocation = Camera.main.transform.position;
            m_grc.RemoveFromScene();
            m_switchFlag = true;


        }

        private void Update()
        {
#if GAIA_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current[m_switchKeyCode].wasPressedThisFrame)
            {
                FlagCharacterSwitch();
            }
#else
            if (Input.GetKeyDown(m_switchKeyCode))
            {
                FlagCharacterSwitch();
            }
#endif
        }


        private void LateUpdate()
        {
            if (m_switchFlag)
            {
                m_switchFlag = false;
                switch (m_grc.m_selectedControllerType)
                {
                    case ControllerType.FirstPerson:
                        m_grc.m_selectedControllerType = ControllerType.ThirdPerson;
                        break;
                    case ControllerType.FlyCam:
                        m_grc.m_selectedControllerType = ControllerType.FirstPerson;
                        break;
                    default:
                        m_grc.m_selectedControllerType = ControllerType.FlyCam;
                        break;
                }
                m_grc.AddToScene();
            }
        }

    }
}