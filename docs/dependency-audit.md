# ЛР №6 — аудит NuGet-залежностей

## Команди

```powershell
dotnet restore
dotnet package list --vulnerable --include-transitive
dotnet package list --vulnerable --include-transitive --format json
```

## Фактичні спостереження

На рисунках 80–81 наведено успішне відновлення та результати аудиту. Для `SecureLab.Api` і `SecureLab.Api.Tests` відомих vulnerable packages з використаних джерел не повідомлено. Джерела — NuGet.org і локальний каталог пакетів SDK. **Decision:** немає підстав оновлювати пакети через конкретну advisory, зафіксовану в цьому аудиті.

|Package / Version|Direct / Transitive|Advisory / Severity|Reachability|Update|Decision|
|-|-|-|-|-|-|
|Не виявлено advisory|Не застосовується|Не виявлено|Не оцінювалася для відсутньої advisory|Не застосовується|Без змін|

**Межа:** результат залежить від доступності advisory-джерел та дати перевірки; невідомі або непубліковані проблеми не виключаються.

