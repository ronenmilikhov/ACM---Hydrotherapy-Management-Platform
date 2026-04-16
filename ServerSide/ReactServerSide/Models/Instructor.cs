namespace ReactServerSide.Models
{
    public class Instructor
    {
        int id;
        string email;
        string passwordhash;
        string firstname;
        string lastname;
        string phone;
        string role; // "Instructor" or "Manager"
        bool isactive;
        DateTime createdat;

        public Instructor(int id, string email, string passwordhash, string firstname, string lastname, string phone, string role, bool isactive, DateTime createdat)
        {
            this.id = id;
            this.email = email;
            this.passwordhash = passwordhash;
            this.firstname = firstname;
            this.lastname = lastname;
            this.phone = phone;
            this.role = role;
            this.isactive = isactive;
            this.createdat = createdat;
        }

        public int Id { get => id; set => id = value; }
        public string Email { get => email; set => email = value; }
        public string Passwordhash { get => passwordhash; set => passwordhash = value; }
        public string Firstname { get => firstname; set => firstname = value; }
        public string Lastname { get => lastname; set => lastname = value; }
        public string Phone { get => phone; set => phone = value; }
        public string Role { get => role; set => role = value; }
        public bool Isactive { get => isactive; set => isactive = value; }
        public DateTime Createdat { get => createdat; set => createdat = value; }
    }
}
