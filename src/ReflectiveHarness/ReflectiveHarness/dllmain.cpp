// dllmain.cpp : Defines the entry point for the DLL application.
#include "stdafx.h"
#include "ReflectiveHarness.h"
#include "ReflectiveLoader.h"
#include "InvokeMain.h"
#include <stdio.h>

extern HINSTANCE hAppInstance;

DWORD ThreadProc(LPVOID arg) {
	
	OutputDebugString(L"Invoking Main Thread");
	InvokeMain();
	return 1;
}

BOOL WINAPI DllMain( HINSTANCE hinstDLL, DWORD dwReason, LPVOID lpReserved )
{
	BOOL bReturnValue = TRUE;
	switch (dwReason)
	{
	case DLL_QUERY_HMODULE:
		if( lpReserved != NULL )
			*(HMODULE *)lpReserved = hAppInstance;
		break;
	case DLL_PROCESS_ATTACH:
		hAppInstance = hinstDLL;
		OutputDebugString(L"In Dll Main");
		
		// Probably not the best way to go about this
		CreateThread(NULL, 0, (LPTHREAD_START_ROUTINE)&ThreadProc, (LPVOID)NULL, 0, NULL);

		break;
	case DLL_THREAD_ATTACH:
	case DLL_THREAD_DETACH:
		break;
	}
	return bReturnValue;
}

