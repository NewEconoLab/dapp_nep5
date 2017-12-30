using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using System;
using System.ComponentModel;
using System.Numerics;

namespace Nep5_Contract
{
    public class ContractNep5 : SmartContract
    {
        //nep5 notify
        public delegate void deleTransfer(byte[] from, byte[] to, BigInteger value);
        [DisplayName("transfer")]
        public static event deleTransfer Transferred;

        public static readonly byte[] SuperAdmin = Helper.ToScriptHash("ALjSnMZidJqd18iQaoCgFun6iqWRm2cVtj");

        //nep5 func
        public static BigInteger TotalSupply()
        {
            return Storage.Get(Storage.CurrentContext, "totalSupply").AsBigInteger();
        }
        public static string Name()
        {
            return "NEP5 Sample Coin";
        }
        public static string Symbol()
        {
            return "NEL";
        }
        private const ulong factor = 100000000;
        private const ulong totalCoin = 100000000 * factor;
        public static byte Decimals()
        {
            return 8;
        }
        public static BigInteger BalanceOf(byte[] address)
        {
            return Storage.Get(Storage.CurrentContext, address).AsBigInteger();
        }
        public static bool Transfer(byte[] from, byte[] to, BigInteger value)
        {
            if (value <= 0) return false;
            if (!Runtime.CheckWitness(from)) return false;
            if (from == to) return true;
            BigInteger from_value = Storage.Get(Storage.CurrentContext, from).AsBigInteger();
            if (from_value < value) return false;
            if (from_value == value)
                Storage.Delete(Storage.CurrentContext, from);
            else
                Storage.Put(Storage.CurrentContext, from, from_value - value);
            BigInteger to_value = Storage.Get(Storage.CurrentContext, to).AsBigInteger();
            Storage.Put(Storage.CurrentContext, to, to_value + value);
            Transferred(from, to, value);
            return true;
        }
        ////增发货币，仅限超级管理员
        //public static bool Deploy(byte[] admin, BigInteger value)
        //{
        //    if (value <= 0) return false;
        //    if (!Runtime.CheckWitness(admin)) return false;

        //    BigInteger total_supply = Storage.Get(Storage.CurrentContext, "totalSupply").AsBigInteger();
        //    BigInteger total_admin = Storage.Get(Storage.CurrentContext, admin).AsBigInteger();
        //    total_supply += value;
        //    total_admin += value;
        //    Storage.Put(Storage.CurrentContext, admin, total_admin);
        //    Storage.Put(Storage.CurrentContext, "totalSupply", total_supply);
        //    return true;
        //}
        ////销毁货币，仅限超级管理员
        //public static bool Destory(byte[] admin, BigInteger value)
        //{
        //    if (value <= 0) return false;
        //    if (!Runtime.CheckWitness(admin)) return false;

        //    BigInteger total_supply = Storage.Get(Storage.CurrentContext, "totalSupply").AsBigInteger();
        //    BigInteger total_admin = Storage.Get(Storage.CurrentContext, admin).AsBigInteger();

        //    if (value > total_admin) return false;

        //    total_supply -= value;
        //    total_admin -= value;
        //    Storage.Put(Storage.CurrentContext, admin, total_admin);
        //    Storage.Put(Storage.CurrentContext, "totalSupply", total_supply);
        //    return true;
        //}
        public static object Main(string method, object[] args)
        {
            var magicstr = "2017-12-26";

            if (Runtime.Trigger == TriggerType.Verification)//取钱才会涉及这里
            {
                return Runtime.CheckWitness(SuperAdmin);
            }
            else if (Runtime.Trigger == TriggerType.Application)
            {
                //this is in nep5
                if (method == "totalSupply") return TotalSupply();
                if (method == "name") return Name();
                if (method == "symbol") return Symbol();
                if (method == "decimals") return Decimals();
                if (method == "balanceOf")
                {
                    if (args.Length != 1) return 0;
                    byte[] account = (byte[])args[0];
                    return BalanceOf(account);
                }
                if (method == "transfer")
                {
                    if (args.Length != 3) return false;
                    byte[] from = (byte[])args[0];
                    byte[] to = (byte[])args[1];
                    BigInteger value = (BigInteger)args[2];
                    return Transfer(from, to, value);
                }
                //this is add
                if (method == "deploy")//fix count
                {
                    if (args.Length != 1) return false;
                    if (!Runtime.CheckWitness(SuperAdmin)) return false;
                    byte[] total_supply = Storage.Get(Storage.CurrentContext, "totalSupply");
                    if (total_supply.Length != 0) return false;

                    Storage.Put(Storage.CurrentContext, SuperAdmin, totalCoin);
                    Storage.Put(Storage.CurrentContext, "totalSupply", totalCoin);
                    Transferred(null, SuperAdmin, totalCoin);
                }
                //if (method == "deploy")
                //{
                //    if (args.Length != 2) return false;
                //    byte[] admin = (byte[])args[0];
                //    BigInteger value = (BigInteger)args[1];
                //    return Deploy(admin, value);
                //}
                //if (method == "destory")
                //{
                //    if (args.Length != 2) return false;
                //    byte[] admin = (byte[])args[0];
                //    BigInteger value = (BigInteger)args[1];
                //    return Destory(admin, value);
                //}
            }
            return false;
        }

    }
}
