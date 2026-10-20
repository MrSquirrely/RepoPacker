### 📝 Pull Request Overview

<!-- Provide a brief, high-level summary of the changes introduced in this PR. -->
#### Description


#### Related Issues
<!-- Link any relevant issues here (e.g., Closes #123). -->
- Closes #

#### Type of Change
- [ ] 🚀 New Feature (non-breaking change adding functionality)
- [ ] 🐛 Bug Fix (non-breaking change fixing an issue)
- [ ] 🎨 UI / Theme Addition or Styling Improvement
- [ ] 📦 Preset Update or Addition (`presets.json`)
- [ ] ⚡ Performance / Memory Optimization
- [ ] 📚 Documentation Update

---

### 🤖 AI Generation & Human Review Disclosure

In accordance with RepoPacker's [Contributing Guidelines](CONTRIBUTING.md), the vast majority of all source code must be synthesized by an AI model under direct human architectural steering.

- **Model(s) Used:**  
  <!-- e.g., Claude 3.7 Sonnet, GPT-4o, Gemini 2.5 Pro -->
- **Estimated Synthetic Percentage:**  
  <!-- e.g., ~90% AI-generated, 10% human-adjusted -->
- **Prompting Strategy & Context Provided:**  
  <!-- Briefly explain how the prompt was framed, which constraints were given (e.g., no var, .NET 10, MVVM), and how the model was guided. -->
- **Human Oversight & Adjustments:**  
  <!-- Detail what you personally reviewed, manually debugged, edited, or corrected in the model's generated output before submitting. -->

---

### 🛡️ Architectural & Technical Checklist

Please verify that your pull request complies with all project rules and architectural standards:

#### 1. Code Standards
- [ ] **Strict Explicit Typing:** Zero instances of the `var` keyword are present in any submitted C# code (e.g., `int count = 5;`, `FileInfo info = new FileInfo(...);`).
- [ ] **Target Framework:** Solution builds cleanly under **.NET 10** (`net10.0-windows`) without warnings or compiler errors.
- [ ] **MVVM Compliance:** All state and user interactions are bound via `CommunityToolkit.Mvvm` attributes and commands.
- [ ] **No Code-Behind Leakage:** `MainWindow.xaml.cs` contains only native `WindowChrome` window-management logic, with zero business or Git logic.

#### 2. Performance & Security
- [ ] **Streaming I/O:** File packing, reading, and token estimation operate via streaming buffers (e.g., 64 KB `char[]` / `byte[]`) rather than massive string concatenations on the Large Object Heap (LOH).
- [ ] **Cancellation Support:** Long-running async tasks accept and observe `CancellationToken`.
- [ ] **Credential Protection:** GitHub Personal Access Tokens are handled securely via Windows DPAPI (`ProtectedData.Protect` with `DataProtectionScope.CurrentUser`) and masked in the UI.

#### 3. UI & Theming
- [ ] **Dynamic Resources:** All brushes and colors use `{DynamicResource ...}` keys so the view adapts to runtime theme switches.
- [ ] **Tested Themes:** Manually verified across both light and dark themes (e.g., **GitHub Dark** and **GitHub Light**).

---

### 🧪 Testing & Verification

<!-- Describe the physical verification steps performed on an actual Windows machine. -->
- **Environment Tested:** Windows 10 / Windows 11 (`.NET 10.0.xxx`)
- **Test Repositories:** 
  <!-- e.g., Tested against small public repo, large private repo with PAT, and mono-repo with subdirectories -->
- **Verification Notes:**
