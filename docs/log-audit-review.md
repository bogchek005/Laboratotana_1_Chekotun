# ЛР №6 — діагностичні журнали та audit trail

## Перевірені властивості

|Сценарій|Очікувана подія|Доказ / статус|
|-|-|-|
|Successful login|Без password і token|У коді ЛР №3 є повідомлення; новий вивід не надано — PENDING|
|Failed login|Відмова без credentials|Окремий вивід у ЛР №6 не надано — PENDING|
|Denied protected action|Reporter 403, без зміни інциденту|403 + denied log — PENDING|
|Validation error|Коректний 4xx без повного request body|HTTP 400 для status, рисунок 15; перевірка log — PARTIAL|
|Unexpected server error|Однаковий traceId у відповіді та log|Рисунки 8–11 — підтверджено в показаному локальному сценарії|
|Privileged transition|Structured success audit event|Рисунки 86, 88–89 — підтверджено|

## Структура аудиту

Подія `incident.triage` містить `Subject`, `Action`, `Resource`, `Result`, `Time`; `success` записується після успішного збереження. Рисунки 86 і 89.

## Локальний журнал

Логи зберігалися поза Git за допомогою `Tee-Object`. Перевірено три шаблони: демонстраційний неправильний пароль, `cookie:|set-cookie` та назву `X-CSRF-TOKEN`. Для них показано 0 збігів, рисунки 90–92.

