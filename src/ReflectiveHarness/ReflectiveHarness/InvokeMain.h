/*
*		Name: InvokeMain
*		
*		Description: Used to invoke C# function included in HarnessDll.h
*
*		Please note that this code was adapted from the PowerTools ReflectivePick and UnmanagedPowerShell projects. All credit goes to their respective authors.
*
*		IMPORTANT NOTES: From the ReflectivePick project:
*		THIS CODE IS ALMOST ENTIRELY FROM UnmanagedPowerShell by Lee Christensen (@tifkin_). It was transformed from an exe format into a
*		Reflective DLL to be used within the PowerPick project. Please recognize that credit for the disovery of this method of running PS code
*		from C++ and all code contained within was his original work. The original executable can be found here: https://github.com/leechristensen/UnmanagedPowerShell
*
*		License: 3-Clause BSD License. 
*
*
*/

#include "stdafx.h"
#pragma region Includes and Imports
#include <windows.h>
#include <comdef.h>
#include <mscoree.h>
#include "HarnessDll.h"

#include <metahost.h>
#pragma comment(lib, "mscoree.lib")

// Import mscorlib.tlb (Microsoft Common Language Runtime Class Library).
#import "mscorlib.tlb" raw_interfaces_only				\
	high_property_prefixes("_get", "_put", "_putref")		\
	rename("ReportEvent", "InteropServices_ReportEvent")
using namespace mscorlib;
#pragma endregion

bool runCheck = false;

extern const unsigned int Harness_dll_len;
extern unsigned char Harness_dll[];
void InvokeMethod(_TypePtr spType, wchar_t* method, wchar_t* command);

void InvokeMain()
{
	OutputDebugString(L"Starting Invoke Main");
	if (runCheck == true)
		return;
	runCheck = true;
	HRESULT hr;

	ICLRMetaHost *pMetaHost = NULL;
	ICLRRuntimeInfo *pRuntimeInfo = NULL;
	ICorRuntimeHost *pCorRuntimeHost = NULL;

	IUnknownPtr spAppDomainThunk = NULL;
	_AppDomainPtr spDefaultAppDomain = NULL;

	// The .NET assembly to load.
	bstr_t bstrAssemblyName("Harness");
	_AssemblyPtr spAssembly = NULL;

	// The .NET class to instantiate.
	bstr_t bstrClassName("Harness.Harness");
	_TypePtr spType = NULL;


	// Start the runtime
	hr = CLRCreateInstance(CLSID_CLRMetaHost, IID_PPV_ARGS(&pMetaHost));
	if (FAILED(hr))
	{
		OutputDebugString(L"CLRCreateInstance failed ");
		goto Cleanup;
	}
	//enumerate and set the proper runtime
	IEnumUnknown* runtimeEnumerator = nullptr;
	hr = pMetaHost->EnumerateInstalledRuntimes(&runtimeEnumerator);
	WCHAR finalRuntime[50];
	if (SUCCEEDED(hr))
	{
		OutputDebugString(L"Got InstalledRuntimes");
		WCHAR currentRuntime[50];
		DWORD bufferSize = ARRAYSIZE(currentRuntime);
		IUnknown* runtime = nullptr;
		while (runtimeEnumerator->Next(1, &runtime, NULL) == S_OK)
		{
			OutputDebugString(L"Enumerating");
			ICLRRuntimeInfo* runtimeInfo = nullptr;
			hr = runtime->QueryInterface(IID_PPV_ARGS(&runtimeInfo));
			
			if (SUCCEEDED(hr))
			{
				hr = runtimeInfo->GetVersionString(currentRuntime, &bufferSize);
				if (SUCCEEDED(hr))
				{
					wcsncpy_s(finalRuntime, currentRuntime, 50);
				}
				runtimeInfo->Release();
			}
			runtime->Release();
		}
		runtimeEnumerator->Release();
	}

	OutputDebugString(L"Done enumerating");
	hr = pMetaHost->GetRuntime(finalRuntime, IID_PPV_ARGS(&pRuntimeInfo));
	if (FAILED(hr))
	{
		OutputDebugString(L"ICLRMetaHost::GetRuntime failed ");
		goto Cleanup;
	}

	// Check if the specified runtime can be loaded into the process.
	BOOL fLoadable;
	hr = pRuntimeInfo->IsLoadable(&fLoadable);
	if (FAILED(hr))
	{
		OutputDebugString(L"ICLRRuntimeInfo::IsLoadable failed ");
		goto Cleanup;
	}

	if (!fLoadable)
	{
		OutputDebugString(L".NET runtime cannot be loaded\n");
		goto Cleanup;
	}

	// Load the CLR into the current process and return a runtime interface
	hr = pRuntimeInfo->GetInterface(CLSID_CorRuntimeHost,
		IID_PPV_ARGS(&pCorRuntimeHost));
	if (FAILED(hr))
	{
		OutputDebugString(L"ICLRRuntimeInfo::GetInterface failed ");
		goto Cleanup;
	}

	// Start the CLR.
	hr = pCorRuntimeHost->Start();
	if (FAILED(hr))
	{
		OutputDebugString(L"CLR failed to start ");
		goto Cleanup;
	}


	// Get a pointer to the default AppDomain in the CLR.
	hr = pCorRuntimeHost->GetDefaultDomain(&spAppDomainThunk);
	if (FAILED(hr))
	{
		OutputDebugString(L"ICorRuntimeHost::GetDefaultDomain failed ");
		goto Cleanup;
	}

	hr = spAppDomainThunk->QueryInterface(IID_PPV_ARGS(&spDefaultAppDomain));
	if (FAILED(hr))
	{
		OutputDebugString(L"Failed to get default AppDomain ");
		goto Cleanup;
	}

	// Load the .NET assembly.
	// (Option 1) Load it from disk - usefully when debugging the PowerShellHarness app (you'll have to copy the DLL into the same directory as the exe)
	//hr = spDefaultAppDomain->Load_2(bstrAssemblyName, &spAssembly);

	// (Option 2) Load the assembly from memory
	SAFEARRAYBOUND bounds[1];
	bounds[0].cElements = Harness_dll_len;
	bounds[0].lLbound = 0;

	SAFEARRAY* arr = SafeArrayCreate(VT_UI1, 1, bounds);
	SafeArrayLock(arr);
	memcpy(arr->pvData, Harness_dll, Harness_dll_len);
	SafeArrayUnlock(arr);

	hr = spDefaultAppDomain->Load_3(arr, &spAssembly);

	if (FAILED(hr))
	{
		OutputDebugString(L"Failed to load the assembly ");
		goto Cleanup;
	}

	// Get the Type of PowerShellHarness.
	hr = spAssembly->GetType_2(bstrClassName, &spType);
	if (FAILED(hr))
	{
		OutputDebugString(L"Failed to get the Type interface ");
		goto Cleanup;
	}

	// Call the static method of the class
	wchar_t* argument = L" ";
	InvokeMethod(spType, L"InvokePS", argument);

Cleanup:
	OutputDebugString(L"Cleaning Up");
	if (pMetaHost)
	{
		pMetaHost->Release();
		pMetaHost = NULL;
	}
	if (pRuntimeInfo)
	{
		pRuntimeInfo->Release();
		pRuntimeInfo = NULL;
	}
	if (pCorRuntimeHost)
	{
		pCorRuntimeHost->Release();
		pCorRuntimeHost = NULL;
	}
	OutputDebugString(L"Returning from InvokeMain");
	return;
}

void InvokeMethod(_TypePtr spType, wchar_t* method, wchar_t* command)
{

	HRESULT hr;
	bstr_t bstrStaticMethodName(method);
	SAFEARRAY *psaStaticMethodArgs = NULL;
	variant_t vtStringArg(command);
	variant_t vtPSInvokeReturnVal;
	variant_t vtEmpty;

	OutputDebugString(L"In InvokeMethod");
	psaStaticMethodArgs = SafeArrayCreateVector(VT_VARIANT, 0, 1);
	LONG index = 0;
	hr = SafeArrayPutElement(psaStaticMethodArgs, &index, &vtStringArg);
	if (FAILED(hr))
	{
		OutputDebugString(L"SafeArrayPutElement failed ");
		return;
	}

	// Invoke the method from the Type interface.
	OutputDebugString(L"Calling InvokeMember_3");
	hr = spType->InvokeMember_3(
		bstrStaticMethodName,
		static_cast<BindingFlags>(BindingFlags_InvokeMethod | BindingFlags_Static | BindingFlags_Public),
		NULL,
		vtEmpty,
		psaStaticMethodArgs,
		&vtPSInvokeReturnVal);
	OutputDebugString(L"REturned from InvokeMember_3");

	if (FAILED(hr))
	{
		OutputDebugString(L"Failed to invoke InvokePS ");
		return;
	}
	else
	{
		// Print the output of the command
		OutputDebugString(vtPSInvokeReturnVal.bstrVal);
	}


	SafeArrayDestroy(psaStaticMethodArgs);
	psaStaticMethodArgs = NULL;
}