using System;

namespace Banking_Application
{
    public abstract class Bank_Account
    {
        public String accountNo;
        public String name;
        public String address_line_1;
        public String address_line_2;
        public String address_line_3;
        public String town;
        public double balance;

        public Bank_Account()
        {
        }

        public Bank_Account(String name, String address_line_1, String address_line_2, String address_line_3, String town, double balance)
        {
            this.accountNo = System.Guid.NewGuid().ToString();
            this.name = name;
            this.address_line_1 = address_line_1;
            this.address_line_2 = address_line_2;
            this.address_line_3 = address_line_3;
            this.town = town;
            this.balance = balance;
        }

        public void lodge(double amountIn)
        {
            balance += amountIn;
        }

        public abstract bool withdraw(double amountToWithdraw);

        public abstract double getAvailableFunds();

        public override String ToString()
        {
            // Reduce PII exposure in console output by masking address fields.
            // Full address is not required for most teller operations (balance/lodge/withdraw).
            return "\nAccount No: " + accountNo + "\n" +
                   "Name: " + (name ?? "") + "\n" +
                   "Address Line 1: " + MaskPii(address_line_1) + "\n" +
                   "Address Line 2: " + MaskPii(address_line_2) + "\n" +
                   "Address Line 3: " + MaskPii(address_line_3) + "\n" +
                   "Town: " + MaskPii(town) + "\n" +
                   "Balance: " + balance + "\n";
        }

        private static string MaskPii(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            value = value.Trim();

            
            if (value.Length <= 4)
                return new string('*', value.Length);

            return value.Substring(0, 2) + new string('*', value.Length - 4) + value.Substring(value.Length - 2, 2);
        }
    }
}
