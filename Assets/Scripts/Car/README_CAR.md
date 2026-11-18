# Player car (CarConcept)

Requires the glTF importer package: add `"com.unity.cloud.gltfast": "6.21.0"` to `Packages/manifest.json`.
The model `Assets/Resources/Cars/CarConcept.glb` is then imported as a GameObject and loaded with
`Resources.Load<GameObject>("Cars/CarConcept")`. License/credits: `Assets/Resources/Cars/ATTRIBUTION.md`.

## Spawn

```csharp
var car = CarSetup.Spawn(spawnPos, Quaternion.LookRotation(spawnForward)); // spawnPos = point on the road
var chase = cameraGO.AddComponent<CarCamera>();   // or GetComponent if already added
chase.target = car.transform;
CarInput.OnToggleFlyRequested += ToggleFly;       // FLY button / F key -> switch to FreeCamera
```

`Spawn` creates `PlayerCar` (layer 8 recursively) with: Rigidbody 1450 kg, 2 BoxColliders (hull + cabin),
4 WheelColliders (`WC_FL/FR/RL/RR`), wheel rigs `Hub_xx/Spin` (calipers steer, wheel+disc spin),
and components `CarController`, `CarInput`, `EngineAudio`, `SpeedHud`.

## Scripts

| File | Role |
|---|---|
| CarSetup.cs | builds the car; finds wheels by name (`WheelFrontL/R`, `WheelRearL/R`), falls back to position |
| CarController.cs | engine curve, 6-speed auto (R when braking at standstill), AWD 35/65, TCS, handbrake drift, downforce, anti-roll, auto-righting after 3 s upside down (`ResetCar()`, `Teleport()`) |
| CarInput.cs | keyboard (W/S, A/D or arrows, Space = handbrake, F = fly) + multitouch OnGUI buttons; `forceTouchControls` shows them on desktop |
| CarCamera.cs | chase camera 5.5 m behind / 1.9 m up, FOV 60 to 72, shake, SphereCast pull-in ignoring layer 8; `Snap()` after teleport |
| EngineAudio.cs | procedural engine (3 RPM layers + intake noise) and tyre squeal, clips generated once in Start |
| SpeedHud.cs | "112 km/h" readout, slanted orange bar (RPM fill), gear badge; sits below the top-right buttons |

Outputs for FX: `CarController.SpeedKmh`, `Rpm`, `Gear` (-1 = R), `SlipAmount` (0..1), `ForwardSpeed`.

When switching to the free camera: disable `CarCamera` and `CarInput` (`OnDisable` zeroes the inputs);
re-enable both to drive again (the FLY button is drawn by `CarInput`, so the free camera needs its own way back).
