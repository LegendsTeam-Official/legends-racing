#!/usr/bin/env bash
# Fast C# compile check without opening Unity (Unity locks the project).
# usage: tools/quickcheck.sh [extra .cs files...]
U=/opt/unity/Editor/Data
ROOT=$(cd "$(dirname "$0")/.." && pwd)
CSC="$U/NetCoreRuntime/dotnet $U/DotNetSdkRoslyn/csc.dll"
REFS=""
for d in "$U"/Managed/UnityEngine/*.dll; do case "$d" in *UnityEditor*) ;; *) REFS="$REFS -r:$d";; esac; done
for d in "$ROOT"/Library/ScriptAssemblies/Unity.RenderPipelines.Core.Runtime.dll "$ROOT"/Library/ScriptAssemblies/Unity.RenderPipelines.Universal.Runtime.dll "$ROOT"/Library/ScriptAssemblies/UnityEngine.UI.dll; do REFS="$REFS -r:$d"; done
NS="-r:$U/NetStandard/ref/2.1.0/netstandard.dll"
OUT=/tmp/qc; mkdir -p $OUT
RT=$(find "$ROOT/Assets/Scripts" -name "*.cs" | grep -v "${EXCLUDE:-@@none@@}")
$CSC -nologo -noconfig -nostdlib -t:library -langversion:9 -nowarn:0168,0219,0414,0649,0162 $NS $REFS -out:$OUT/rt.dll $RT "$@" 2>&1 | grep -E "error|warning CS0(108|114)" | head -40
echo "runtime exit: ${PIPESTATUS[0]}"
EDREFS="-r:$U/Managed/UnityEditor.dll"
for d in "$U"/Managed/UnityEngine/UnityEditor*.dll; do EDREFS="$EDREFS -r:$d"; done
for d in "$ROOT"/Library/ScriptAssemblies/Unity.RenderPipelines.Core.Editor.dll "$ROOT"/Library/ScriptAssemblies/Unity.RenderPipelines.Universal.Editor.dll; do EDREFS="$EDREFS -r:$d"; done
ED=$(find "$ROOT/Assets/Editor" -name "*.cs")
[ -f $OUT/rt.dll ] && $CSC -nologo -noconfig -nostdlib -t:library -langversion:9 -nowarn:0168,0219,0414,0649 $NS $REFS $EDREFS -r:$OUT/rt.dll -out:$OUT/ed.dll $ED 2>&1 | grep -E "error" | head -40
echo "editor exit: ${PIPESTATUS[0]}"
