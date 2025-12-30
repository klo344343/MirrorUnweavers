# 🌀 Mirror Unweaver Engine

**Mirror Unweaver** is a specialized tool for reverse engineering and deep cleaning of .NET assemblies (DLLs) processed by the Mirror networking framework. It allows you to revert modified code back to its original, "clean" state.

## ✨ Key Features
* **Logic Restoration**: Automatically locates `UserCode_...` methods and restores their content back into the original methods.
* **Smart Hierarchy Search**: If a network property (e.g., `NetworkTargetState`) was stripped, the tool finds the original field or property within base classes (e.g., from `PryableDoor` to `DoorVariant`).
* **Safe Stack Cleanup**: Automatically injects `pop` instructions when removing Mirror calls to prevent program crashes (`InvalidProgramException`).
* **Garbage Removal**: Completely strips away Mirror's technical methods (`OnSerialize`, `InvokeUserCode`) and the `GeneratedNetworkCode` class.

## 🛠 Tech Stack
* **Language**: C#.
* **Library**: [dnlib](https://github.com/0xd4d/dnlib) — for advanced IL-code manipulation.

## 🚀 Usage
1. Compile the project.
2. Drag and drop your `Assembly-CSharp.dll` onto the executable or run it via console:
   ```bash
   MirrorUnweaver.exe "path/to/your/library.dll"
3. The cleaned file will be saved with the _Unweaved.dll suffix.
