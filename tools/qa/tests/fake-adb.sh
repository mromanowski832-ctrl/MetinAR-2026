#!/usr/bin/env bash
# Simulated adb for exercising Invoke-MetinARDeviceSmoke.ps1 without a phone (CI / Linux only).
# Output is synthetic; a run against this script is NOT physical-device evidence.
# FAKE_ADB_MODE=ok|nodevice|crash|adblost|adbblip|metinarexc|unityexc|logcatempty|logcaterr|logcatnoapp   FAKE_ADB_PACKAGE (default com.metinar.game.qa)
# #90 session modes (Invoke-MetinARDeviceSession.ps1): session|sessionlow|sessionnocombo - a 150 s AR encounter timeline with
# [MetinARPerf] 5 s windows, combat ([ArDamage] swing=) and combo ([Combo] step=) lines after the 60 s warm-up; the first
# `logcat -d` returns the first 75 s, later dumps the whole timeline (exercises the de-duplicating merge).
# sessionlow = 24 fps / p95 60 ms in combat; sessionnocombo = no [Combo] lines.
# Pulled videos contain 'fake-mp4'; `shell stat -c %s` / `shell sha256sum` of /sdcard files report that content
# (FAKE_ADB_VIDEO_SHA_BAD=1: a different SHA-256, i.e. the copy does not match the phone).
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
  pull) printf 'fake-mp4' > "$3"; echo "$2: 1 file pulled. (fake)"; exit 0;;
  logcat)
    if [ "$2" = "-c" ]; then touch "${state}.cleared"; exit 0; fi
    case "$mode" in session|sessionlow|sessionnocombo)
      lc=$(( $(cat "${state}.logcat" 2>/dev/null || echo 0) + 1 )); echo "$lc" > "${state}.logcat"
      last=150; [ "$lc" -eq 1 ] && last=75
      echo "--------- beginning of main"
      # a crash of an EARLIER run, present until `logcat -c`; the collector must drop it by time when the buffer is kept
      [ -f "${state}.cleared" ] || { echo "10-20 04:10:00.000  3999  3999 E AndroidRuntime: FATAL EXCEPTION: main"; echo "10-20 04:10:00.001  3999  3999 E AndroidRuntime: Process: $pkg, PID: 3999"; }
      echo "10-20 04:29:59.000  1500  1600 I ActivityManager: Start proc 4242:$pkg/u0a321 for top-activity"
      echo "10-20 04:30:00.100  4242  4242 I Unity   : [MetinARPerf] v=1 event=start mainThreadRecorder=1 device=FakePhone"
      echo "10-20 04:30:00.200  4242  4242 I Unity   : [WorldMapPrototype] AR ENCOUNTER REQUESTED (button)"
      echo "10-20 04:30:01.000  4242  4242 I Unity   : [ArEncounter] state Starting -> Scanning"
      echo "10-20 04:30:03.000  4242  4242 I Unity   : [ArEncounter] spawned Pajak at (0.1, -1.2, 2.0) anchor=Plane"
      echo "10-20 04:30:04.000  4242  4242 I Unity   : GPS fix lat=52.229770 lon=21.011780 serial FAKESERIAL123"
      for s in $(seq 5 5 $last); do
        mm=$((30 + s / 60)); ss=$(printf '%02d' $((s % 60))); cs=$(printf '%02d' $(( (s - 3) % 60 )))
        cm=$((30 + (s - 3) / 60))
        fps=29.6; p50=33.3; p95=35; spk=0
        if [ "$mode" = "sessionlow" ] && [ "$s" -gt 60 ]; then fps=24; p50=41; p95=60; spk=3; fi
        if [ "$s" -eq 35 ] || [ "$s" -gt 60 ]; then
          echo "10-20 04:$cm:$cs.500  4242  4260 I Unity   : [ArDamage] swing=$s outcome=Hit hp=100/120"
          [ "$mode" != "sessionnocombo" ] && echo "10-20 04:$cm:$cs.600  4242  4260 I Unity   : [Combo] step=$(( (s / 5) % 3 + 1 )) weapon=1H hit=yes swing=$s"
        fi
        echo "10-20 04:$mm:$ss.000  4242  4260 I Unity   : [MetinARPerf] v=1 scene=ArEncounterPrototype windowS=5 frames=148 fpsAvg=$fps frameAvgMs=33.8 frameP50Ms=$p50 frameP95Ms=$p95 frameMaxMs=45 frameSpikes=$spk mainAvgMs=20 mainP95Ms=30 mainMaxMs=40 mainSpikes=0 totalUsedMB=420 gcReservedMB=40 systemUsedMB=600"
      done
      [ "$last" -eq 150 ] && echo "10-20 04:32:30.500  4242  4260 I Unity   : [ArDamage] defeated pajak exp=+40 talary=+12"
      exit 0;;
    esac
    [ "$mode" = "logcatempty" ] && exit 0
    if [ "$mode" = "logcaterr" ]; then
      echo "10-20 04:30:18.123  4242  4242 I Unity   : [MetinARPerf] v=1 event=start mainThreadRecorder=1 device=FakePhone"
      echo "logcat: Unexpected EOF!" >&2; exit 1
    fi
    if [ "$mode" = "logcatnoapp" ]; then
      echo "10-20 04:30:18.500  1500  1600 I ActivityManager: Displayed com.other.app/.Main: +2s100ms"
      echo "10-20 04:30:19.000   999   999 I chatty  : uid=1000 system_server expire 3 lines"
      exit 0
    fi
    echo "10-20 04:30:18.123  4242  4242 I Unity   : [MetinARPerf] v=1 event=start mainThreadRecorder=1 device=FakePhone"
    for i in 1 2 3; do
      echo "10-20 04:30:2$i.200  4242  4260 I Unity   : [MetinARPerf] v=1 scene=WorldMapPrototype windowS=5 frames=300 fpsAvg=60 frameAvgMs=16.67 frameP50Ms=16.6 frameP95Ms=18 frameMaxMs=40 frameSpikes=0 mainAvgMs=8 mainP95Ms=10 mainMaxMs=20 mainSpikes=0 totalUsedMB=250 gcReservedMB=30 systemUsedMB=400"
    done
    echo "10-20 04:30:29.000  4242  4242 I Unity   : GPS fix lat=52.229770 lon=21.011780 from 192.168.1.23 serial FAKESERIAL123"
    if [ "$mode" = "metinarexc" ] || [ "$mode" = "unityexc" ]; then
      echo "10-20 04:30:29.500  4242  4260 E Unity   : NullReferenceException: Object reference not set to an instance of an object."
      echo "10-20 04:30:29.500  4242  4260 E Unity   :   at UnityEngine.Events.InvokableCall.Invoke () [0x00000] in <00000000000000000000000000000000>:0 "
      if [ "$mode" = "metinarexc" ]; then
        echo "10-20 04:30:29.500  4242  4260 E Unity   :   at MetinAR.Combat.ArDamageDealer.TryHit (System.Int32 swingId) [0x00000] in <00000000000000000000000000000000>:0 "
      else
        echo "10-20 04:30:29.500  4242  4260 E Unity   :   at UnityEngine.XR.ARFoundation.ARSession.Update () [0x00000] in <00000000000000000000000000000000>:0 "
      fi
      echo "10-20 04:30:29.500  4242  4260 E Unity   : (Filename: currently not available on il2cpp Line: -1)"
    fi
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
      "date +%m-%d_%H:%M:%S") echo "10-20_04:29:58";;
      "getprop ro.product.manufacturer") echo "FakeVendor";;
      "dumpsys gfxinfo $pkg framestats") echo "Total frames rendered: 120"; echo "Janky frames: 6 (5.00%)"; echo "50th percentile: 33ms"; echo "90th percentile: 34ms"; echo "95th percentile: 36ms"; echo "99th percentile: 50ms"
        echo "---PROFILEDATA---"; echo "Flags,IntendedVsync,Vsync,OldestInputEvent,NewestInputEvent,HandleInputStart,AnimationStart,PerformTraversalsStart,DrawStart,SyncQueued,SyncStart,IssueDrawCommandsStart,SwapBuffers,FrameCompleted,"
        for i in 0 1 2 3; do v=$((1000000000 + i*33333333)); echo "0,$v,$v,0,0,0,0,0,0,0,0,0,0,$((v + 30000000 + i*1000000)),"; done; echo "1,5,5,0,0,0,0,0,0,0,0,0,0,999999999,"; echo "---PROFILEDATA---";;
      "dumpsys gfxinfo $pkg reset") ;;
      screenrecord*) sleep 1;;
      stat\ -c\ %s\ /sdcard/*) echo "8";;
      sha256sum\ /sdcard/*) if [ "${FAKE_ADB_VIDEO_SHA_BAD:-0}" = 1 ]; then h=$(printf 'other' | sha256sum | cut -d' ' -f1); else h=$(printf 'fake-mp4' | sha256sum | cut -d' ' -f1); fi; echo "$h  $2";;
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
      "dumpsys meminfo $pkg")
        case "$mode" in session*) mc=$(( $(cat "${state}.mem" 2>/dev/null || echo 0) + 1 )); echo "$mc" > "${state}.mem"; echo "               TOTAL PSS:   $((400000 + mc * 1024))            TOTAL RSS:   500000";;
          *) echo "               TOTAL PSS:   312345            TOTAL RSS:   400000";; esac;;
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
