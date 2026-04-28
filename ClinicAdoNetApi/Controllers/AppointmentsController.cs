using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using ClinicAdoNetApi.DTOs;

namespace ClinicAdoNetApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AppointmentsController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public AppointmentsController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpGet]
    public async Task<IActionResult> GetAppointments(
        [FromQuery] string? status,
        [FromQuery] string? patientLastName)
    {
        var appointments = new List<AppointmentListDto>();
        var connectionString = _configuration.GetConnectionString("DefaultConnection");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var command = new SqlCommand(@"
            SELECT
                a.IdAppointment,
                a.AppointmentDate,
                a.Status,
                a.Reason,
                p.FirstName + N' ' + p.LastName AS PatientFullName,
                p.Email AS PatientEmail
            FROM dbo.Appointments a
            JOIN dbo.Patients p ON p.IdPatient = a.IdPatient
            WHERE (@Status IS NULL OR a.Status = @Status)
              AND (@PatientLastName IS NULL OR p.LastName = @PatientLastName)
            ORDER BY a.AppointmentDate;
        ", connection);

        command.Parameters.Add("@Status", SqlDbType.NVarChar, 30).Value =
            (object?)status ?? DBNull.Value;

        command.Parameters.Add("@PatientLastName", SqlDbType.NVarChar, 80).Value =
            (object?)patientLastName ?? DBNull.Value;

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            appointments.Add(new AppointmentListDto
            {
                IdAppointment = reader.GetInt32(0),
                AppointmentDate = reader.GetDateTime(1),
                Status = reader.GetString(2),
                Reason = reader.GetString(3),
                PatientFullName = reader.GetString(4),
                PatientEmail = reader.GetString(5)
            });
        }

        return Ok(appointments);
    }

    [HttpGet("{idAppointment:int}")]
    public async Task<IActionResult> GetAppointmentById([FromRoute] int idAppointment)
    {
        var connectionString = _configuration.GetConnectionString("DefaultConnection");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var command = new SqlCommand(@"
            SELECT
                a.IdAppointment,
                a.AppointmentDate,
                a.Status,
                a.Reason,
                a.InternalNotes,
                a.CreatedAt,
                p.IdPatient,
                p.FirstName + N' ' + p.LastName AS PatientFullName,
                p.Email,
                p.PhoneNumber,
                d.IdDoctor,
                d.FirstName + N' ' + d.LastName AS DoctorFullName,
                d.LicenseNumber,
                s.Name AS SpecializationName
            FROM dbo.Appointments a
            JOIN dbo.Patients p ON p.IdPatient = a.IdPatient
            JOIN dbo.Doctors d ON d.IdDoctor = a.IdDoctor
            JOIN dbo.Specializations s ON s.IdSpecialization = d.IdSpecialization
            WHERE a.IdAppointment = @IdAppointment;
        ", connection);

        command.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return NotFound(new { message = "Appointment not found." });
        }

        var appointment = new AppointmentDetailsDto
        {
            IdAppointment = reader.GetInt32(0),
            AppointmentDate = reader.GetDateTime(1),
            Status = reader.GetString(2),
            Reason = reader.GetString(3),
            InternalNotes = reader.IsDBNull(4) ? null : reader.GetString(4),
            CreatedAt = reader.GetDateTime(5),
            IdPatient = reader.GetInt32(6),
            PatientFullName = reader.GetString(7),
            PatientEmail = reader.GetString(8),
            PatientPhone = reader.IsDBNull(9) ? null : reader.GetString(9),
            IdDoctor = reader.GetInt32(10),
            DoctorFullName = reader.GetString(11),
            DoctorLicenseNumber = reader.GetString(12),
            SpecializationName = reader.GetString(13)
        };

        return Ok(appointment);
    }

    [HttpPost]
    public async Task<IActionResult> CreateAppointment([FromBody] CreateAppointmentRequestDto request)
    {
        if (request.AppointmentDate <= DateTime.Now)
            return BadRequest(new { message = "Appointment date cannot be in the past." });

        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 250)
            return BadRequest(new { message = "Reason is required and must have max 250 characters." });

        var connectionString = _configuration.GetConnectionString("DefaultConnection");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var patientCommand = new SqlCommand("""
            SELECT COUNT(1)
            FROM dbo.Patients
            WHERE IdPatient = @IdPatient AND IsActive = 1;
        """, connection);

        patientCommand.Parameters.Add("@IdPatient", SqlDbType.Int).Value = request.IdPatient;

        var patientExists = (int)await patientCommand.ExecuteScalarAsync()!;

        if (patientExists == 0)
            return BadRequest(new { message = "Patient does not exist or is inactive." });

        var doctorCommand = new SqlCommand("""
            SELECT COUNT(1)
            FROM dbo.Doctors
            WHERE IdDoctor = @IdDoctor AND IsActive = 1;
        """, connection);

        doctorCommand.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = request.IdDoctor;

        var doctorExists = (int)await doctorCommand.ExecuteScalarAsync()!;

        if (doctorExists == 0)
            return BadRequest(new { message = "Doctor does not exist or is inactive." });

        var conflictCommand = new SqlCommand("""
            SELECT COUNT(1)
            FROM dbo.Appointments
            WHERE IdDoctor = @IdDoctor
              AND AppointmentDate = @AppointmentDate
              AND Status = N'Scheduled';
        """, connection);

        conflictCommand.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = request.IdDoctor;
        conflictCommand.Parameters.Add("@AppointmentDate", SqlDbType.DateTime2).Value = request.AppointmentDate;

        var conflictExists = (int)await conflictCommand.ExecuteScalarAsync()!;

        if (conflictExists > 0)
            return Conflict(new { message = "Doctor already has a scheduled appointment at this time." });

        var insertCommand = new SqlCommand("""
            INSERT INTO dbo.Appointments (IdPatient, IdDoctor, AppointmentDate, Status, Reason)
            OUTPUT INSERTED.IdAppointment
            VALUES (@IdPatient, @IdDoctor, @AppointmentDate, N'Scheduled', @Reason);
        """, connection);

        insertCommand.Parameters.Add("@IdPatient", SqlDbType.Int).Value = request.IdPatient;
        insertCommand.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = request.IdDoctor;
        insertCommand.Parameters.Add("@AppointmentDate", SqlDbType.DateTime2).Value = request.AppointmentDate;
        insertCommand.Parameters.Add("@Reason", SqlDbType.NVarChar, 250).Value = request.Reason;

        var newId = (int)await insertCommand.ExecuteScalarAsync()!;

        return CreatedAtAction(
            nameof(GetAppointmentById),
            new { idAppointment = newId },
            new { idAppointment = newId }
        );
    }

    [HttpPut("{idAppointment:int}")]
    public async Task<IActionResult> UpdateAppointment(
        [FromRoute] int idAppointment,
        [FromBody] UpdateAppointmentRequestDto request)
    {
        if (!new[] { "Scheduled", "Completed", "Cancelled" }.Contains(request.Status))
            return BadRequest(new { message = "Invalid status." });

        if (request.AppointmentDate <= DateTime.Now)
            return BadRequest(new { message = "Appointment date cannot be in the past." });

        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 250)
            return BadRequest(new { message = "Reason is required and must have max 250 characters." });

        var connectionString = _configuration.GetConnectionString("DefaultConnection");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var checkCommand = new SqlCommand("""
            SELECT Status, AppointmentDate
            FROM dbo.Appointments
            WHERE IdAppointment = @IdAppointment;
        """, connection);

        checkCommand.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;

        await using var checkReader = await checkCommand.ExecuteReaderAsync();

        if (!await checkReader.ReadAsync())
            return NotFound(new { message = "Appointment not found." });

        var currentStatus = checkReader.GetString(0);
        var currentDate = checkReader.GetDateTime(1);

        await checkReader.CloseAsync();

        if (currentStatus == "Completed" && request.AppointmentDate != currentDate)
            return Conflict(new { message = "Cannot change date of completed appointment." });

        var patientCommand = new SqlCommand("""
            SELECT COUNT(1)
            FROM dbo.Patients
            WHERE IdPatient = @IdPatient AND IsActive = 1;
        """, connection);

        patientCommand.Parameters.Add("@IdPatient", SqlDbType.Int).Value = request.IdPatient;

        var patientExists = (int)await patientCommand.ExecuteScalarAsync()!;

        if (patientExists == 0)
            return BadRequest(new { message = "Patient does not exist or is inactive." });

        var doctorCommand = new SqlCommand("""
            SELECT COUNT(1)
            FROM dbo.Doctors
            WHERE IdDoctor = @IdDoctor AND IsActive = 1;
        """, connection);

        doctorCommand.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = request.IdDoctor;

        var doctorExists = (int)await doctorCommand.ExecuteScalarAsync()!;

        if (doctorExists == 0)
            return BadRequest(new { message = "Doctor does not exist or is inactive." });

        var conflictCommand = new SqlCommand("""
            SELECT COUNT(1)
            FROM dbo.Appointments
            WHERE IdDoctor = @IdDoctor
              AND AppointmentDate = @AppointmentDate
              AND IdAppointment <> @IdAppointment
              AND Status = N'Scheduled';
        """, connection);

        conflictCommand.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = request.IdDoctor;
        conflictCommand.Parameters.Add("@AppointmentDate", SqlDbType.DateTime2).Value = request.AppointmentDate;
        conflictCommand.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;

        var conflictExists = (int)await conflictCommand.ExecuteScalarAsync()!;

        if (conflictExists > 0)
            return Conflict(new { message = "Doctor already has appointment at this time." });

        var updateCommand = new SqlCommand("""
            UPDATE dbo.Appointments
            SET IdPatient = @IdPatient,
                IdDoctor = @IdDoctor,
                AppointmentDate = @AppointmentDate,
                Status = @Status,
                Reason = @Reason,
                InternalNotes = @InternalNotes
            WHERE IdAppointment = @IdAppointment;
        """, connection);

        updateCommand.Parameters.Add("@IdPatient", SqlDbType.Int).Value = request.IdPatient;
        updateCommand.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = request.IdDoctor;
        updateCommand.Parameters.Add("@AppointmentDate", SqlDbType.DateTime2).Value = request.AppointmentDate;
        updateCommand.Parameters.Add("@Status", SqlDbType.NVarChar, 20).Value = request.Status;
        updateCommand.Parameters.Add("@Reason", SqlDbType.NVarChar, 250).Value = request.Reason;
        updateCommand.Parameters.Add("@InternalNotes", SqlDbType.NVarChar, 500).Value =
            (object?)request.InternalNotes ?? DBNull.Value;
        updateCommand.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;

        await updateCommand.ExecuteNonQueryAsync();

        return Ok();
    }

    [HttpDelete("{idAppointment:int}")]
    public async Task<IActionResult> DeleteAppointment([FromRoute] int idAppointment)
    {
        var connectionString = _configuration.GetConnectionString("DefaultConnection");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var checkCommand = new SqlCommand("""
            SELECT Status
            FROM dbo.Appointments
            WHERE IdAppointment = @IdAppointment;
        """, connection);

        checkCommand.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;

        var status = (string?)await checkCommand.ExecuteScalarAsync();

        if (status == null)
            return NotFound(new { message = "Appointment not found." });

        if (status == "Completed")
            return Conflict(new { message = "Cannot delete completed appointment." });

        var deleteCommand = new SqlCommand("""
            DELETE FROM dbo.Appointments
            WHERE IdAppointment = @IdAppointment;
        """, connection);

        deleteCommand.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;

        await deleteCommand.ExecuteNonQueryAsync();

        return NoContent();
    }
}