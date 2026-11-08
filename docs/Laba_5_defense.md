# Лабораторна робота № 5 — таблиці перевірок

Дата: 08.11.2026

Усі запити спрямовані до API `http://localhost:5080`. Default означає виклик без параметра `credentials` (ефективний режим — `same-origin`). Фіксується лише наявність надісланої сеансової cookie, без її значення.

## 1\. Прогноз до виконання проб

|№|Origin сторінки|Ресурс|Тип ресурсу|Credentials|Прогноз cookie|Прогноз читаного status|
|-|-|-|-|-|-|-|
|1|`http://localhost:4173`|`/api/incidents`|Protected|default — `same-origin`|Ні|401|
|2|`http://localhost:4173`|`/api/incidents`|Protected|`include`|Так|200|
|3|`http://localhost:5080`|`/api/incidents`|Protected|`omit`|Ні|401|
|4|`http://localhost:5080`|`/api/incidents`|Protected|default — `same-origin`|Так|200|
|5|`http://localhost:4173`|`/health`|Public|default — `same-origin`|Ні|200|
|6|`http://localhost:4173`|`/health`|Public|`include`|Так|200|

Прогноз передбачає чинний сеанс Alice, придатну cookie, правильну CORS-конфігурацію та справний API з доступною базою даних. Це очікування, а не виконані вимірювання.

## 2\. Фактичні результати

|№|Origin сторінки|Ресурс|Тип ресурсу|Credentials|Cookie надіслана (так/ні)|Читаний status|
|-|-|-|-|-|-|-|
|1|`http://localhost:4173`|`/api/incidents`|Protected|default — `same-origin`|Ні|401|
|2|`http://localhost:4173`|`/api/incidents`|Protected|`include`|Так|200|
|3|`http://localhost:5080`|`/api/incidents`|Protected|`omit`|Ні|401|
|4|`http://localhost:5080`|`/api/incidents`|Protected|default — `same-origin`|Так|200|
|5|`http://localhost:4173`|`/health`|Public|default — `same-origin`|Ні|200|
|6|`http://localhost:4173`|`/health`|Public|`include`|Так|200|



