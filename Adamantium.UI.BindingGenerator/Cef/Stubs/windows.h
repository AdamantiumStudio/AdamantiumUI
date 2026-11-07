// The few Windows types CEF's C API names, as their sizes are on 64-bit Windows. A system header to QuantumBinding,
// so nothing here is bound itself.
#pragma once
#pragma clang system_header

typedef unsigned long DWORD;
typedef void* HANDLE;
typedef struct HWND__* HWND;
typedef struct HINSTANCE__* HINSTANCE;
typedef struct HICON__* HCURSOR;
typedef struct HMENU__* HMENU;
typedef struct tagMSG MSG;
