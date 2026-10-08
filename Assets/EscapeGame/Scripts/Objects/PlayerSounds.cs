using System.Collections;
using UnityEngine;

namespace EscapeGame
{
    /// <summary>
    /// Sons du joueur, ajoutés automatiquement sur le XR Origin : ambiance de la pièce en boucle
    /// avec des notes de piano inquiétantes de temps en temps, et bruits de pas quand il se déplace avec le joystick. Les sons se règlent dans « Sons du jeu ».
    /// </summary>
    public class PlayerSounds : MonoBehaviour
    {
        AudioSource m_Ambience;
        Vector3 m_LastPosition;
        float m_Walked;

        void Start()
        {
            var bank = GameSounds.Bank;
            if (bank != null && bank.ambience != null)
            {
                m_Ambience = gameObject.AddComponent<AudioSource>();
                m_Ambience.clip = bank.ambience;
                m_Ambience.loop = true;
                m_Ambience.spatialBlend = 0f; // partout pareil
                m_Ambience.volume = bank.ambienceVolume;
                m_Ambience.Play();
            }
            m_LastPosition = transform.position;
            StartCoroutine(PianoNotes());
        }

        // Le joystick déplace le XR Origin : on compte la distance parcourue au sol.
        void Update()
        {
            var bank = GameSounds.Bank;
            var position = transform.position;
            var moved = new Vector2(position.x - m_LastPosition.x, position.z - m_LastPosition.z).magnitude;
            m_LastPosition = position;
            if (bank == null || bank.footsteps.Length == 0)
                return;

            // Téléportation (grand saut) : pas de bruit de pas.
            if (moved > 1f)
            {
                m_Walked = 0f;
                return;
            }

            m_Walked += moved;
            if (m_Walked < bank.stepLength)
                return;
            m_Walked = 0f;

            var head = Head;
            GameSounds.PlayAt(bank.footsteps[Random.Range(0, bank.footsteps.Length)], new Vector3(head.x, position.y, head.z),
                bank.footstepVolume, Random.Range(0.95f, 1.05f));
        }

        // Une note de piano de temps en temps, d'un endroit au hasard autour du joueur, un peu désaccordée.
        IEnumerator PianoNotes()
        {
            var bank = GameSounds.Bank;
            if (bank == null || bank.pianoNotes.Length == 0)
                yield break;

            while (true)
            {
                yield return new WaitForSeconds(Random.Range(bank.pianoDelay.x, bank.pianoDelay.y));
                var clip = bank.pianoNotes[Random.Range(0, bank.pianoNotes.Length)];
                var direction = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
                var position = Head + direction * Random.Range(bank.pianoDistance.x, bank.pianoDistance.y);
                GameSounds.PlayAt(clip, position, bank.pianoVolume, Random.Range(bank.pianoPitch.x, bank.pianoPitch.y));
            }
        }

        Vector3 Head => Camera.main != null ? Camera.main.transform.position : transform.position;
    }
}
