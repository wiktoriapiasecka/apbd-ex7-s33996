# ClinicAdoNetApi

Prosta aplikacja ASP.NET Core Web API do obsługi wizyt w przychodni.
Projekt wykonany w celu przećwiczenia ADO.NET (bez Entity Framework).

## Funkcjonalności

API umożliwia:

* pobieranie listy wizyt
* pobieranie szczegółów wizyty
* dodawanie nowych wizyt
* edytowanie wizyt
* usuwanie wizyt

## Technologie

* ASP.NET Core Web API (.NET 8)
* ADO.NET (Microsoft.Data.SqlClient)
* SQL Server (Docker)
* Swagger

## Baza danych

Baza danych uruchomiona w Dockerze (SQL Server 2022).
Skrypt `01_create_and_seed_clinic.sql` tworzy tabele i dane testowe.

## Endpointy

* GET `/api/appointments`
* GET `/api/appointments/{id}`
* POST `/api/appointments`
* PUT `/api/appointments/{id}`
* DELETE `/api/appointments/{id}`

## Uruchomienie

1. Uruchomić SQL Server w Dockerze
2. Wykonać skrypt SQL
3. Ustawić connection string w `appsettings.json`
4. Uruchomić aplikację

## Uwagi

* Zapytania SQL są parametryzowane (brak SQL Injection)
* Dane mapowane są ręcznie na DTO
* Obsługiwane są podstawowe błędy i statusy HTTP

---

Projekt wykonany w ramach zajęć APBD.
