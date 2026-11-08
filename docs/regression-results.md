# ЛР №6 — контрольний regression pack

|ID|Напрям|Негативний сценарій|Позитивний сценарій|Фактичний статус|
|-|-|-|-|-|
|ID ЛР №2 — звірити з файлами тестів|Validation|400 для недопустимого status, рисунок 15|-|PARTIAL|
|ID ЛР №2 — звірити|SQL injection|-|Search 200, рисунок 79|PARTIAL|
|ID ЛР №3 — звірити|Authentication|`/api/me` 401 після logout|Logout із токеном 204, рисунки 68–69|PARTIAL|
|ID ЛР №4 — звірити|Authorization / IDOR|Reporter 403 не продемонстровано|Дозволений triage 204, рисунки 73–74|PARTIAL|
|REG-XSS-01|XSS|Безпечний DOM підтверджений у ЛР №5|Буквальний текст після виправлення|PASS раніше / RECHECK|
|REG-CORS-01|CORS|4174 заборонено у ЛР №5|4173 дозволено у ЛР №5|PASS раніше / RECHECK|
|REG-CSRF-01|CSRF|POST, PUT, DELETE, logout, triage без токена 400|POST 201, logout/triage 204|PASS|
|ID rate-limit — звірити|Rate limiting|200, 200, 200, 429|Recovery 200, search 200;|PARTIAL|
|Повний набір|dotnet test|Даних немає|Даних немає|PENDING|

Завершальний прогін в корені проєкту:

```powershell
dotnet restore
dotnet build SecureLab.sln -c Release
dotnet test SecureLab.sln -c Release
```

Після запуску додати фактичні passed/failed/total, час і commit hash.

