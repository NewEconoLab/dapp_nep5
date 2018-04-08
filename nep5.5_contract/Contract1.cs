using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using Helper = Neo.SmartContract.Framework.Helper;
using System;
using System.ComponentModel;
using System.Numerics;

namespace Nep5_Contract
{
    public class ContractNep55 : SmartContract
    {
        //在nep5标准上追加几个要求，暂定nep5.5标准
        //1.对接口("transfer",[from,to,value]) 检查 entry 和 callscript 一致性，禁止跳板
        //2.追加接口("transfer_app",[from,to,value])，当from == callscript 时通过,将鉴权扩展到应用合约
        //3.追加接口("gettxinfo",[txid]),返回[from,to,value],每笔转账都写入记录，使用当前txid做key
        //    智能合约可以用此接口检查一笔NEP5转账的详情，只能查到已经发生过的交易


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
            return "NEP5.5 Sample Coin";
        }
        public static string Symbol()
        {
            return "N55";
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

            if (from == to) return true;
            BigInteger from_value = Storage.Get(Storage.CurrentContext, from).AsBigInteger();
            if (from_value < value) return false;
            if (from_value == value)
                Storage.Delete(Storage.CurrentContext, from);
            else
                Storage.Put(Storage.CurrentContext, from, from_value - value);
            BigInteger to_value = Storage.Get(Storage.CurrentContext, to).AsBigInteger();
            Storage.Put(Storage.CurrentContext, to, to_value + value);

            //记录交易信息
            TransferInfo info = new TransferInfo();
            info.from = from;
            info.to = to;
            info.value = value;
            byte[] txinfo = Helper.Serialize(info);
            var txid = (ExecutionEngine.ScriptContainer as Transaction).Hash;
            Storage.Put(Storage.CurrentContext, txid, txinfo);
            Transferred(from, to, value);
            return true;
        }
        public class TransferInfo
        {
            public byte[] from;
            public byte[] to;
            public BigInteger value;
        }
        public static TransferInfo gettxinfo(byte[] txid)
        {
            byte[] v = Storage.Get(Storage.CurrentContext, txid);
            if (v.Length == 0)
                return null;
            return Helper.Deserialize(v) as TransferInfo;
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
            var magicstr = "2018-04-06";

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

                    //没有from签名，不让转
                    if (!Runtime.CheckWitness(from))
                        return false;
                    //如果有跳板调用，不让转
                    if (ExecutionEngine.EntryScriptHash.AsBigInteger() != ExecutionEngine.CallingScriptHash.AsBigInteger())
                        return false;

                }
                if (method == "transfer_app")
                {
                    if (args.Length != 3) return false;
                    byte[] from = (byte[])args[0];
                    byte[] to = (byte[])args[1];
                    BigInteger value = (BigInteger)args[2];

                    //如果from 不是 传入脚本 不让转
                    if (from.AsBigInteger() != ExecutionEngine.CallingScriptHash.AsBigInteger())
                        return false;

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
