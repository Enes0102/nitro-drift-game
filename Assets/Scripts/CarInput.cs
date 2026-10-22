using UnityEngine;
using UnityEngine.InputSystem;

// PC: A/D veya ok tuşları, S/aşağı = fren, Shift = nitro, Space = drift
// Mobil: ekranın sol yarısına dokun = sola, sağ yarısına dokun = sağa
[RequireComponent(typeof(CarController))]
public class CarInput : MonoBehaviour
{
    public bool autoGas = true;     // sonsuz sürüş: gaz kendiliğinden basılı

    CarController car;

    void Awake() { car = GetComponent<CarController>(); }

    void Update()
    {
        float steer = 0f;
        float gas = autoGas ? 1f : 0f;
        bool nitro = false, drift = false;

        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) steer -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) steer += 1f;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) gas = 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) gas = -1f;
            nitro = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            drift = kb.spaceKey.isPressed;
        }

        var ts = Touchscreen.current;
        if (ts != null)
        {
            foreach (var t in ts.touches)
            {
                if (!t.press.isPressed) continue;
                steer += t.position.ReadValue().x < Screen.width * 0.5f ? -1f : 1f;
            }
        }

        car.steerInput = Mathf.Clamp(steer, -1f, 1f);
        car.gasInput = gas;
        car.nitroInput = nitro;
        car.driftInput = drift;
    }
}