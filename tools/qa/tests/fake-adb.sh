#!/usr/bin/env bash
# Simulated adb for exercising Invoke-MetinARDeviceSmoke.ps1 without a phone (CI / Linux only).
# Output is synthetic; a run against this script is NOT physical-device evidence.
# FAKE_ADB_MODE=ok|nodevice|crash|adblost|adbblip   FAKE_ADB_PACKAGE (default com.metinar.game.qa)
# adblost: `adb devices` lists the phone for the first FAKE_ADB_LOST_AFTER calls (default 3), then never again.
# adbblip: the phone is missing from `adb devices` exactly once (call number FAKE_ADB_LOST_AFTER + 1), then back.
mode="${FAKE_ADB_MODE:-ok}"
state="${FAKE_ADB_STATE:-/tmp/fake-adb-state}"
pkg="${FAKE_ADB_PACKAGE:-com.metinar.game.qa}"
lostafter="${FAKE_ADB_LOST_AFTER:-3}"
[ "$1" = "-s" ] && shift 2
devcount_file="${state}.devices"
case "$1" in
  devices)
    n=$(( $(cat "$devcount_file" 2>/dev/null || echo 0) + 1 )); echo "$n" > "$devcount_file"
    echo "List of devices attached"
    show=1
    [ "$mode" = "nodevice" ] && show=0
    [ "$mode" = "adblost" ] && [ "$n" -gt "$lostafter" ] && show=0
    [ "$mode" = "adbblip" ] && [ "$n" -eq $((lostafter + 1)) ] && show=0
    [ "$show" = 1 ] && printf 'FAKESERIAL123\tdevice\n'
    exit 0;;
  install) echo "Performing Streamed Install"; echo "Success"; exit 0;;
  logcat)
    if [ "$2" = "-c" ]; then exit 0; fi
    echo "10-20 04:30:18.123  4242  4242 I Unity   : [MetinARPerf] v=1 event=start mainThreadRecorder=1 device=FakePhone"
    for i in 1 2 3; do
      echo "10-20 04:30:2$i.200  4242  4260 I Unity   : [MetinARPerf] v=1 scene=WorldMapPrototype windowS=5 frames=300 fpsAvg=60 frameAvgMs=16.67 frameP50Ms=16.6 frameP95Ms=18 frameMaxMs=40 frameSpikes=0 mainAvgMs=8 mainP95Ms=10 mainMaxMs=20 mainSpikes=0 totalUsedMB=250 gcReservedMB=30 systemUsedMB=400"
    done
    echo "10-20 04:30:29.000  4242  4242 I Unity   : GPS fix lat=52.229770 lon=21.011780 from 192.168.1.23 serial FAKESERIAL123"
    if [ "$mode" = "crash" ]; then
      echo "10-20 04:30:30.000  4242  4242 E AndroidRuntime: FATAL EXCEPTION: main"
      echo "10-20 04:30:30.001  4242  4242 E AndroidRuntime: Process: $pkg, PID: 4242"
    fi
    exit 0;;
  shell)
    shift
    cmd="$*"
    case "$cmd" in
      "getprop ro.product.model") echo "FakePhone";;
      "getprop ro.build.version.release") echo "12";;
      "getprop ro.build.version.sdk") echo "31";;
      "getprop ro.product.cpu.abi") echo "arm64-v8a";;
      "pm list packages $pkg") echo "package:$pkg";;
      cmd\ package\ resolve-activity*) echo "priority=0 preferredOrder=0"; echo "$pkg/com.unity3d.player.UnityPlayerGameActivity";;
      am\ force-stop*) ;;
      am\ start\ -W*) echo "Starting: Intent { cmp=$pkg/com.unity3d.player.UnityPlayerGameActivity }"; echo "Status: ok"; echo "LaunchState: COLD"; echo "TotalTime: 2345"; echo "WaitTime: 2400"; echo "Complete";;
      am\ start*) echo "Starting: Intent"; rm -f "$state";;
      "dumpsys window") echo "  mCurrentFocus=Window{1 u0 $pkg/com.unity3d.player.UnityPlayerGameActivity}";;
      "pidof $pkg") echo "4242";;
      "dumpsys activity activities") if [ -f "$state" ]; then echo "  topResumedActivity=ActivityRecord{1 u0 com.google.android.apps.nexuslauncher/.NexusLauncherActivity}"; else echo "  topResumedActivity=ActivityRecord{1 u0 $pkg/com.unity3d.player.UnityPlayerGameActivity t5}"; fi;;
      "dumpsys package $pkg") printf '    runtime permissions:\n      android.permission.ACCESS_FINE_LOCATION: granted=true, flags=[ USER_SET ]\n      android.permission.CAMERA: granted=false, flags=[ USER_SET ]\n';;
      "dumpsys meminfo $pkg") echo "               TOTAL PSS:   312345            TOTAL RSS:   400000";;
      "dumpsys gfxinfo $pkg") echo "Total frames rendered: 0"; echo "Janky frames: 0 (0.00%)";;
      "dumpsys SurfaceFlinger --list") echo "SurfaceView[$pkg/com.unity3d.player.UnityPlayerGameActivity]#0"; echo "SurfaceView[$pkg/com.unity3d.player.UnityPlayerGameActivity](BLAST)#1";;
      dumpsys\ SurfaceFlinger\ --latency*) echo "16666666"; for i in $(seq 0 60); do t=$((1000000000 + i*16666666)); echo "$t	$t	$t"; done;;
      "dumpsys cpuinfo") echo "  45% 4242/$pkg: 40% user + 5% kernel";;
      "dumpsys thermalservice") echo "Thermal Status: 0";;
      "dumpsys battery") echo "  level: 80"; echo "  temperature: 312";;
      "input keyevent KEYCODE_HOME") touch "$state";;
      "dumpsys location") echo "  Location Listeners:";;
      *) ;;
    esac
    exit 0;;
esac
exit 0
