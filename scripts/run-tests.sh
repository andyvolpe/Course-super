#!/usr/bin/env bash
# Greenkeeper test gate.
#
# Runs the EditMode keystone suite (T1-T4 + phase tests). Unity's Test Framework IS NUnit, and the
# sim is pure C#, so the exact same test files run headless under `dotnet test` — no Unity needed.
# This is what makes "zero errors" permanent (see docs/greenkeeper-model.md).
#
# If you have the Unity Editor installed and prefer the in-engine runner, use instead:
#   "<Unity>" -runTests -batchmode -projectPath . -testPlatform EditMode \
#             -testResults ./TestResults.xml -logFile -
set -euo pipefail
cd "$(dirname "$0")/.."

if ! command -v dotnet >/dev/null 2>&1; then
  echo "error: dotnet SDK not found. Install .NET 8 SDK to run the headless test gate." >&2
  exit 1
fi

echo "==> Building pure-C# sim (asserts zero UnityEngine dependency)..."
dotnet build headless/Greenkeeper.Sim/Greenkeeper.Sim.csproj -v q --nologo

echo "==> Running EditMode keystone tests (T1-T4)..."
dotnet test headless/Greenkeeper.Tests/Greenkeeper.Tests.csproj -v q --nologo
