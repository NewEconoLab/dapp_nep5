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

        //nep5.5gas 加入用gas兑换的部分，和退回gas的功能
        //4.追加接口("mintTokens",[])，自动将当前交易的输出中的GAS兑换为等量的该NEP5资产
        //逻辑和mintTokens是一样的，就保持一致吧
        //5.追加接口("exchangeUTXO",[who]),自动将当前交易输出中的gas，标记为who可提取，同时销毁who的等量NEP5资产
        //      之后可发起一笔转账 input为这个标记的utxo，output 为who，取走其中的GAS

        //storage1 map<address:hash160,balancce:biginteger>     //nep5余额表
        //storage2 map<txid:hash256,balance:txinfo>             //nep5交易信息表
        //storage3 map<utxo:hash256+n,targetaddress:hash256>    //utxo授权表

        //nep5 notify
        public delegate void deleTransfer(byte[] from, byte[] to, BigInteger value);
        [DisplayName("transfer")]
        public static event deleTransfer Transferred;

        //gas 0x602c79718b16e442de58778e148d0b1084e3b2dffd5de6b7b16cee7969282de7
        //反序  e72d286979ee6cb1b7e65dfddfb2e384100b8d148e7758de42e4168b71792c60
        private static readonly byte[] gas_asset_id = Helper.HexToBytes("e72d286979ee6cb1b7e65dfddfb2e384100b8d148e7758de42e4168b71792c60");
        //nep5 func
        public static BigInteger TotalSupply()
        {
            return Storage.Get(Storage.CurrentContext, "totalSupply").AsBigInteger();
        }
        public static string Name()
        {
            return "NEP5.5 Coin With GAS 1:1";
        }
        public static string Symbol()
        {
            return "SGAS";
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

            //付款方
            if (from.Length > 0)
            {
                BigInteger from_value = Storage.Get(Storage.CurrentContext, from).AsBigInteger();
                if (from_value < value) return false;
                if (from_value == value)
                    Storage.Delete(Storage.CurrentContext, from);
                else
                    Storage.Put(Storage.CurrentContext, from, from_value - value);
            }
            //收款方
            if (to.Length > 0)
            {
                BigInteger to_value = Storage.Get(Storage.CurrentContext, to).AsBigInteger();
                Storage.Put(Storage.CurrentContext, to, to_value + value);
            }
            //记录交易信息
            TransferInfo info = new TransferInfo();
            info.from = from;
            info.to = to;
            info.value = value;
            byte[] txinfo = Helper.Serialize(info);
            var txid = (ExecutionEngine.ScriptContainer as Transaction).Hash;
            Storage.Put(Storage.CurrentContext, txid, txinfo);

            //notify
            Transferred(from, to, value);
            return true;
        }
        //public static bool Mint(byte[] to, BigInteger value)
        //{
        //    if (value <= 0) return false;

        //    BigInteger to_value = Storage.Get(Storage.CurrentContext, to).AsBigInteger();
        //    Storage.Put(Storage.CurrentContext, to, to_value + value);

        //    //记录交易信息
        //    TransferInfo info = new TransferInfo();
        //    info.from = null;
        //    info.to = to;
        //    info.value = value;
        //    byte[] txinfo = Helper.Serialize(info);
        //    var txid = (ExecutionEngine.ScriptContainer as Transaction).Hash;
        //    Storage.Put(Storage.CurrentContext, txid, txinfo);

        //    //notify
        //    Transferred(null, to, value);
        //    return true;
        //}
        //public static bool Refund(byte[] from, BigInteger value)
        //{
        //    if (value <= 0) return false;

        //    //付款方
        //    BigInteger from_value = Storage.Get(Storage.CurrentContext, from).AsBigInteger();
        //    if (from_value < value) return false;
        //    if (from_value == value)
        //        Storage.Delete(Storage.CurrentContext, from);
        //    else
        //        Storage.Put(Storage.CurrentContext, from, from_value - value);


        //    //记录交易信息
        //    TransferInfo info = new TransferInfo();
        //    info.from = from;
        //    info.to = null;
        //    info.value = value;
        //    byte[] txinfo = Helper.Serialize(info);
        //    var txid = (ExecutionEngine.ScriptContainer as Transaction).Hash;
        //    Storage.Put(Storage.CurrentContext, txid, txinfo);

        //    //notify
        //    Transferred(from, null, value);
        //    return true;
        //}
        public class TransferInfo
        {
            public byte[] from;
            public byte[] to;
            public BigInteger value;
        }
        public static TransferInfo GetTXInfo(byte[] txid)
        {
            byte[] v = Storage.Get(Storage.CurrentContext, txid);
            if (v.Length == 0)
                return null;
            return Helper.Deserialize(v) as TransferInfo;
        }

        public static bool MintToken()
        {
            var tx = ExecutionEngine.ScriptContainer as Transaction;

            //获取投资人，谁要换gas
            byte[] who = null;
            TransactionOutput[] reference = tx.GetReferences();
            for (var i = 0; i < reference.Length; i++)
            {
                if (reference[i].AssetId.AsBigInteger() == gas_asset_id.AsBigInteger())
                {
                    who = reference[i].ScriptHash;
                    break;
                }
            }

            TransactionOutput[] outputs = tx.GetOutputs();
            ulong value = 0;
            // get the total amount of Gas
            // 获取转入智能合约地址的Gas总量
            foreach (TransactionOutput output in outputs)
            {
                if (output.ScriptHash == ExecutionEngine.ExecutingScriptHash &&
                    output.AssetId.AsBigInteger() == gas_asset_id.AsBigInteger())
                {
                    value += (ulong)output.Value;
                }
            }

            //改变总量
            var total_supply = Storage.Get(Storage.CurrentContext, "totalSupply").AsBigInteger();
            total_supply += value;
            Storage.Put(Storage.CurrentContext, "totalSupply", total_supply);

            //1:1 不用换算
            return Transfer(null, who, value);

        }
        //退款
        public static bool RefundToken(byte[] who)
        {
            var tx = ExecutionEngine.ScriptContainer as Transaction;
            var outputs = tx.GetOutputs();
            if (outputs.Length > 1)
                return false;

            var count = outputs[0].Value;
            //退款tx 肯定只有一个输出 并且转给本合约自身

            //退的不是gas，不行
            if (outputs[0].AssetId.AsBigInteger() != gas_asset_id.AsBigInteger())
                return false;
            //不是转给自身，不行
            if (outputs[0].ScriptHash.AsBigInteger() != ExecutionEngine.ExecutingScriptHash.AsBigInteger())
                return false;

            //当前的交易已经名花有主了，不行
            byte[] coinid = tx.Hash.Concat(new byte[] { 0, 0 });
            byte[] target = Storage.Get(Storage.CurrentContext, coinid);
            if (target.Length > 0)
                return false;

            //改变总量
            var total_supply = Storage.Get(Storage.CurrentContext, "totalSupply").AsBigInteger();
            total_supply -= count;
            Storage.Put(Storage.CurrentContext, "totalSupply", total_supply);

            return Transfer(who, null, count);
        }
        public static object Main(string method, object[] args)
        {
            var magicstr = "2018-04-06";

            if (Runtime.Trigger == TriggerType.Verification)//取钱才会涉及这里
            {
                //return Runtime.CheckWitness(SuperAdmin);
                var tx = ExecutionEngine.ScriptContainer as Transaction;
                var curhash = ExecutionEngine.ExecutingScriptHash;
                var inputs = tx.GetInputs();
                var outputs = tx.GetOutputs();

                //检查输入是不是有被标记过
                for (var i = 0; i < inputs.Length; i++)
                {
                    byte[] coinid = inputs[i].PrevHash.Concat(new byte[] { 0, 0 });
                    byte[] target = Storage.Get(Storage.CurrentContext, coinid);

                    if (target.Length > 0)
                    {
                        if (inputs.Length > 1 || outputs.Length != 1)//使用标记coin的时候只允许一个输入\一个输出
                            return false;

                        //如果只有一个输入，一个输出，并且目的转账地址就是授权地址
                        //允许转账
                        if (outputs[0].ScriptHash.AsBigInteger() == target.AsBigInteger())
                            return true;
                        else//否则不允许
                            return false;
                    }
                }
                //走到这里没跳出，说明输入都没有被标记

                //检查有没有钱离开本合约
                for (var i = 0; i < outputs.Length; i++)
                {
                    if (outputs[i].ScriptHash.AsBigInteger() != curhash.AsBigInteger())
                    {
                        return false;
                    }
                }
                //没有资金离开本合约地址，允许
                return true;
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
                    if (from == to)
                        return true;
                    if (from.Length == 0 || to.Length == 0)
                        return false;


                    BigInteger value = (BigInteger)args[2];

                    //没有from签名，不让转
                    if (!Runtime.CheckWitness(from))
                        return false;
                    //如果有跳板调用，不让转
                    if (ExecutionEngine.EntryScriptHash.AsBigInteger() != ExecutionEngine.CallingScriptHash.AsBigInteger())
                        return false;

                    return Transfer(from, to, value);
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
                if (method == "gettxinfo")
                {
                    if (args.Length != 1) return 0;
                    byte[] txid = (byte[])args[0];
                    return GetTXInfo(txid);
                }
                if (method == "mintTokens")
                {
                    if (args.Length != 0) return 0;
                    return MintToken();
                }
                if (method == "exchangeUTXO")
                {
                    if (args.Length != 1) return 0;
                    byte[] who = (byte[])args[0];
                    if (!Runtime.CheckWitness(who))
                        return false;
                    return RefundToken(who);
                }
            }
            return false;
        }

    }
}
