# Chatbot Fix V3

- Increased Gemini `maxOutputTokens` from 900 to 4096.
- Concatenates all Gemini response text parts instead of reading only `parts[0]`.
- Detects `finishReason = MAX_TOKENS` and uses the deterministic database fallback rather than showing a cut-off answer.
- Keeps prompt-leak protection and CompanyId-grounded database context from V2.

## Rebuild

```powershell
docker compose down
docker compose up -d --build
```

Then hard refresh the browser (`Ctrl + Shift + R`).
