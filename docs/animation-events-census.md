# Animation event handlers: functor census

Step 1 and 2 of the event investigation (`docs/animation-variables.md` §4): every
`<Name>Handler` registered for `IHandlerFunctor<Actor, BSFixedStringCI>`, its functor
(slot 1 of its vftable) and what the functor calls, from `index.py summary`. Input,
menu and UI handlers that share the name suffix are left out. Empty `calls` means
the functor returns without calling anything: the event exists for the engine to
send or a script to wait on, and the game does nothing on receiving it.

```
ActionActivateDoneHandler 0x1407b9670 calls: 140179710 1402eac20 1406de6d0 1406e4230 1406e4260 
AddRagdollHandler 0x1407b9960 calls: 1401949b0 1402b8d30 14054e200 14054e280 140655a00 1406568e0 1406628f0 1406d1ee0 
AllowRotationHandler 0x1407b9d40 calls: 1407c2c10 
AnimatedCameraDeltaStartHandler 0x1407ba890 calls: 1408e42d0 
AnimatedCameraEndHandler 0x1407ba8c0 calls: 1408e4460 
AnimatedCameraStartHandler 0x1407ba860 calls: 1408e42d0 
AnimationDrivenHandler 0x1407b9cd0 calls: 1407c2c10 
AnimationObjectDrawHandler 0x1407ba5e0 calls: 1401e0250 1402efac0 1407c3570 140cec9c0 
AnimationObjectLoadHandler 0x1407ba560 calls: 1401e0250 1402efac0 1407c32b0 140cec9c0 
AnticipateAttackHandler 0x1407b8240 calls: 14014ef60 1406b9330 
ArrowAttachHandler 0x1407b90a0 calls: 14070db20 
ArrowDetachHandler 0x1407b9160 calls: 14070db20 
ArrowReleaseHandler 0x1407b9230 calls: 14014c1f0 1401dc910 1402233c0 140223400 140286c90 1406c6d90 14070db20 
AttackStopHandler 0x1407b7b40 calls: 1406ee160 
AttackWinEndHandler 0x1407b7b00 calls: 
AttackWinStartHandler 0x1407b7ac0 calls: 
BedEnterHandler 0x1407b8500 calls: 14069c6d0 1406c0f00 140711b60 140712780 1407127c0 
BedFurnitureExitHandler 0x1407b87b0 calls: 14069c6d0 140711b60 
BowDrawnHandler 0x1407b9060 calls: 
BowReleaseHandler 0x1407b9080 calls: 
BowZoomStartHandler 0x1407b93d0 calls: 140385f30 14067c1d0 1406b76c0 1407440c0 140cc97e0 
BowZoomStopHandler 0x1407b94b0 calls: 14067c1d0 140744200 140cc97e0 
CameraOverrideStartHandler 0x1407b8130 calls: 140674930 1408e5930 
CameraOverrideStopHandler 0x1407b8170 calls: 140674930 1408e5930 
CameraShakeHandler 0x1407b98b0 calls: 1405506d0 140cec9c0 
ChairEnterHandler 0x1407b82a0 calls: 14069c6d0 1406c0f00 140711b60 140712780 1407127c0 
ChairFurnitureExitHandler 0x1407b86e0 calls: 14069c6d0 140711b60 
DeathEmoteHandler 0x1407b9930 calls: 1406e1c00 
DeathStopHandler 0x1407b9650 calls: 
DecapitateHandler 0x1407b8a70 calls: 140685140 
DisableCharacterBumperHandler 0x1407ba4c0 calls: 1402ef290 140655ec0 1406628f0 1406d1ee0 140e9ec60 
DisableCharacterPitchHandler 0x1407ba690 calls: 1406628f0 
EnableCharacterBumperHandler 0x1407ba420 calls: 1402ef290 140655ec0 1406628f0 1406d1ee0 140e9ec60 
EnableCharacterPitchHandler 0x1407ba660 calls: 1406628f0 
EndSummonAnimationHandler 0x1407b9600 calls: 1406c5c20 
ExitCartBeginHandler 0x1407ba2d0 calls: 1402fe9d0 14069b3c0 
ExitCartEndHandler 0x1407ba300 calls: 140699080 
FlightActionEndHandler 0x1407b8d60 calls: 140655d90 140691ea0 1406d1ee0 
FlightActionEntryEndHandler 0x1407b8d40 calls: 140691cd0 
FlightActionGrabHandler 0x1407b8db0 calls: 
FlightActionHandler 0x1407b8cc0 calls: 140655d90 1406567c0 140691ea0 14069be40 1406d1ee0 
FlightActionReleaseHandler 0x1407b8dd0 calls: 
FlightCrashLandStartHandler 0x1407b8df0 calls: 140179710 1401e14a0 1402e0170 1402ea6a0 1402ea960 1402efac0 1404afa20 1404b00f0 1404ef3e0 14067e120 140693050 140
FlightCruisingHandler 0x1407b8b10 calls: 1406567c0 14069be40 1406d1ee0 
FlightHoveringHandler 0x1407b8b60 calls: 1406567c0 14069be40 1406d1ee0 
FlightLandEndHandler 0x1407b8ca0 calls: 1406b9ed0 
FlightLandHandler 0x1407b8c50 calls: 1406567c0 14069be40 1406d1ee0 
FlightLandingHandler 0x1407b8bb0 calls: 1406567c0 14069be40 1406d1ee0 
FlightPerchingHandler 0x1407b8c00 calls: 1406567c0 14069be40 1406d1ee0 
FlightTakeOffHandler 0x1407b8ac0 calls: 1406567c0 14069be40 1406d1ee0 
GetUpEndHandler 0x1407ba170 calls: 14014ef60 1402e5770 140655920 140655a90 140655ad0 140655b10 1406c6e00 
GetUpStartHandler 0x1407b9fc0 calls: 14014ef60 1402b8d30 140656470 1406628f0 1406d1ee0 140e9e9c0 140ea06e0 14153b7a0 
HeadTrackingOffHandler 0x1407b8aa0 calls: 140692970 
HeadTrackingOnHandler 0x1407b8a90 calls: 
HitFrameHandler 0x1407b81b0 calls: 14014ef60 1406b9460 
IdleDialogueEnterHandler 0x1407ba780 calls: 
IdleDialogueExitHandler 0x1407ba790 calls: 140179710 1406dde70 1406ec820 1406ec860 
InterruptCastHandler 0x1407b95d0 calls: 1406c3bd0 
JumpAnimEventHandler 0x1407ba6d0 calls: 140280270 1402e09c0 1406628f0 140b7ae60 140e9ea30 
KillActorHandler 0x1407b8920 calls: 1401795f0 1402efac0 1405482f0 140664f80 1406b9ed0 1406b9ef0 140cc67e0 140cc6b10 140cc7060 
KillMoveEndHandler 0x1407b9910 calls: 1406b9dc0 
KillMoveStartHandler 0x1407b9900 calls: 
LeftHandSpellCastHandler 0x1407b7d60 calls: 1405bf030 
LeftHandSpellFireHandler 0x1407b7ba0 calls: 1405bbd40 
MTStateHandler 0x1407ba940 calls: 140713080 
MotionDrivenHandler 0x1407b9c60 calls: 1407c2c10 
MountDismountEndHandler 0x1407ba250 calls: 140673960 1406c0800 
NPCAttachHandler 0x1407ba330 calls: 140179710 1402fe880 1407127c0 140e18620 
NPCDetachHandler 0x1407ba400 calls: 1402fe9d0 
PairedStopHandler 0x1407b97e0 calls: 14069c6d0 1406c0f00 140711b60 140712780 1407127c0 14077a100 
PickNewIdleHandler 0x1407b9620 calls: 1406de6d0 
PitchOverrideEndHandler 0x1407ba910 calls: 1408e5950 
PitchOverrideStartHandler 0x1407ba8e0 calls: 1408e5950 
PlayerBedEnterHandler 0x1407b85f0 calls: 14069c6d0 1406c0f00 140711b60 140712780 1407127c0 1408e3a10 
PlayerChairEnterHandler 0x1407b8390 calls: 140179710 14069c6d0 1406c0f00 140711b60 140712780 1407127c0 1408e3a10 14097adb0 140cd5650 140cd56e0 
PlayerFurnitureExitHandler 0x1407b8880 calls: 14069c6d0 140711b60 1408e3a10 14097adb0 140cd5700 
RagdollStartHandler 0x1407b9e30 calls: 1401949b0 14054e3a0 140656470 1406628f0 1406d1ee0 140e9e9c0 
RecoilStopHandler 0x1407b7b80 calls: 
RemoveRagdollHandler 0x1407b9db0 calls: 1402b8d30 140655a90 1406d1ee0 140e87150 
RightHandSpellCastHandler 0x1407b7db0 calls: 1405bf030 
RightHandSpellFireHandler 0x1407b7bf0 calls: 1405bbd40 
RightHandWeaponDrawHandler 0x1407b7fb0 calls: 140280270 14065d270 140674930 1406b76c0 1407159c0 14072ec90 1408e5850 14091eda0 strings: ['WarHorseMode'] 
RightHandWeaponSheatheHandler 0x1407b8110 calls: 14065d2b0 
StaggeredStopHandler 0x1407b8280 calls: 
StopMountCameraHandler 0x1407b9750 calls: 14069c6d0 1406bfb10 140711b60 
VampireFeedEndHandler 0x1407ba990 calls: 14068ea90 1406d6b00 1406de6d0 
VoiceSpellCastHandler 0x1407b7e00 calls: 1405bf030 1406ed480 
VoiceSpellFireHandler 0x1407b7c40 calls: 140381010 1405bbd40 1406ed3b0 1406ed460 1406ed480 1406ed4a0 1406ed4c0 1406ed560 
WeaponBeginDrawRightHandler 0x1407b7e90 calls: 140280270 140551af0 14065d270 140674930 1408e5850 
WeaponBeginSheatheRightHandler 0x1407b7f40 calls: 140551af0 14065d270 
WeaponLeftSwingHandler 0x1407b7a30 calls: 140551af0 14065d270 
WeaponRightSwingHandler 0x1407b79a0 calls: 140551af0 14065d270 
ZeroPitchHandler 0x1407ba970 calls: 1408e5970 
```

Shared callees, which is where step 3 starts: `0x1407c2c10` (AnimationDriven, MotionDriven,
AllowRotation -- one setter of the motion mode); `0x1405bf030` / `0x1405bbd40` (every spell
cast / fire); `0x14070db20` (arrow attach, detach, release); `0x1406567c0` + `0x14069be40` +
`0x1406d1ee0` (every flight state); `0x14069c6d0` + `0x140711b60` (every furniture enter and
exit, and PairedStop, StopMountCamera); `0x1408e42d0` / `0x1408e4460` (animated camera);
`0x1408e5950` (pitch override); `0x1406628f0` (the character controller, on pitch, bumper,
ragdoll, get-up and jump); `0x1406de6d0` (pick a new idle: PickNewIdle, ActionActivateDone,
VampireFeedEnd).
