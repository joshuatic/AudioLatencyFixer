#include "pch.h"
#include <windows.h>

extern "C" __declspec(dllexport)
void EnableLowLatencyMode()
{
    // Boost process priority
    SetPriorityClass(GetCurrentProcess(), HIGH_PRIORITY_CLASS);
}