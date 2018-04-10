using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using Helper = Neo.SmartContract.Framework.Helper;
using System;
using System.ComponentModel;
using System.Numerics;

namespace Nft_Contract
{
    public class ContractNFT : SmartContract
    {
        public static readonly byte[] superAdmin = Helper.ToScriptHash("ALjSnMZidJqd18iQaoCgFun6iqWRm2cVtj");

        //nft notify
        public delegate void deleTransfer(byte[] nftid, byte[] from, byte[] to);
        [DisplayName("transfer")]
        public static event deleTransfer Transferred;

        //nft 总体信息
        public static BigInteger totalSupply()
        {
            return Storage.Get(Storage.CurrentContext, "totalSupply").AsBigInteger();
        }
        public static string name()
        {
            return "NFT TEST";
        }
        public static string symbol()
        {
            return "NFT01";
        }

        //map<00+nftid,owner>
        //map<01+nftid,data>
        //map<txid,transferinfo>

        private static bool _transfer(byte[] nftid, byte[] from, byte[] to)
        {

            if (from == to) return true;

            ////转出方
            if (from.Length > 0)
            {
                var owner = Storage.Get(Storage.CurrentContext, new byte[] { 0x00 }.Concat(nftid));
                if (owner.AsBigInteger() != from.AsBigInteger())
                    return false;
            }
            if (to.Length == 0)
            {
                Storage.Delete(Storage.CurrentContext, new byte[] { 0x00 }.Concat(nftid));
            }
            else
            {
                Storage.Put(Storage.CurrentContext, new byte[] { 0x00 }.Concat(nftid), to);
            }

            //记录交易信息
            TransferInfo info = new TransferInfo();
            info.nftid = nftid;
            info.from = from;
            info.to = to;
            byte[] txinfo = Helper.Serialize(info);
            var txid = (ExecutionEngine.ScriptContainer as Transaction).Hash;
            Storage.Put(Storage.CurrentContext, txid, txinfo);

            //notify
            Transferred(nftid, from, to);
            return true;
        }
        //Transfer(转让所有权) 有两个实现
        //一个("transfer",[...])//用checkwitness(from)鉴证
        //一个("transfer_app",[...])//用callscript==from 鉴证，鉴证过程推给应用合约
        public static bool transfer(byte[] nftid, byte[] from, byte[] to)
        {
            //没有from签名，不让转
            if (!Runtime.CheckWitness(from))
                return false;
            //如果有跳板调用，不让转
            if (ExecutionEngine.EntryScriptHash.AsBigInteger() != ExecutionEngine.CallingScriptHash.AsBigInteger())
                return false;

            return _transfer(nftid, from, to);
        }
        public static bool transfer_app(byte[] nftid, byte[] from, byte[] to)
        {
            //如果from 不是 传入脚本 不让转
            if (from.AsBigInteger() != ExecutionEngine.CallingScriptHash.AsBigInteger())
                return false;

            return _transfer(nftid, from, to);
        }

        //上两个操作都会产生交易记录

        //查询交易记录

        public class TransferInfo
        {
            public byte[] nftid;
            public byte[] from;
            public byte[] to;
        }
        public static TransferInfo getTXInfo(byte[] txid)
        {
            byte[] v = Storage.Get(Storage.CurrentContext, txid);
            if (v.Length == 0)
                return null;
            return Helper.Deserialize(v) as TransferInfo;
        }

        public static byte[] getNFTOwner(byte[] nftid)
        {
            var owner = Storage.Get(Storage.CurrentContext, new byte[] { 0x00 }.Concat(nftid));
            return owner;
        }
        public static byte[] getNFTData(byte[] nftid)
        {
            var owner = Storage.Get(Storage.CurrentContext, new byte[] { 0x01 }.Concat(nftid));
            return owner;
        }

        public static byte[] makeNFT(byte[] data)
        {
            //只能超级管理员来
            if (Runtime.CheckWitness(superAdmin) == false)
                return null;
            if (data.Length > 2048)
                return null;
            var nftid = SmartContract.Sha256(data);
            var owner = Storage.Get(Storage.CurrentContext, new byte[] { 0x00 }.Concat(nftid));
            if (owner.Length > 0)
            {
                //已经存在不得重复创建
                return null;
            }
            Storage.Put(Storage.CurrentContext, new byte[] { 0x00 }.Concat(nftid), superAdmin);
            Storage.Put(Storage.CurrentContext, new byte[] { 0x01 }.Concat(nftid), data);
            _transfer(nftid, null, superAdmin);
            return nftid;
        }
        public static object Main(string method, object[] args)
        {
            var magicstr = "2018-04-10";

            if (Runtime.Trigger == TriggerType.Verification)//取钱才会涉及这里
            {
                return false;
            }
            else if (Runtime.Trigger == TriggerType.Application)
            {
                //this is in nep5
                if (method == "totalSupply") return totalSupply();
                if (method == "name") return name();
                if (method == "symbol") return symbol();
                if (method == "transfer")
                {
                    if (args.Length != 3) return false;
                    byte[] nftid = (byte[])args[0];
                    byte[] from = (byte[])args[1];
                    byte[] to = (byte[])args[2];
                    if (from == to)
                        return true;
                    if (from.Length == 0 || to.Length == 0)
                        return false;


                    return transfer(nftid, from, to);
                }
                if (method == "transfer_app")
                {
                    if (args.Length != 3) return false;
                    byte[] nftid = (byte[])args[0];
                    byte[] from = (byte[])args[1];
                    byte[] to = (byte[])args[2];
                    if (from == to)
                        return true;
                    if (from.Length == 0 || to.Length == 0)
                        return false;


                    return transfer_app(nftid, from, to);
                }
                if (method == "getTXInfo")
                {
                    if (args.Length != 1) return 0;
                    byte[] txid = (byte[])args[0];
                    return getTXInfo(txid);
                }
                if (method == "getNFTOwner")
                {
                    if (args.Length != 1) return 0;
                    byte[] nftid = (byte[])args[0];
                    return getNFTOwner(nftid);
                }
                if (method == "getNFTData")
                {
                    if (args.Length != 1) return 0;
                    byte[] nftid = (byte[])args[0];
                    return getNFTData(nftid);
                }
                if (method == "makeNFT")
                {
                    if (args.Length != 1) return 0;
                    byte[] data = (byte[])args[0];
                    return makeNFT(data);
                }
            }
            return false;
        }

    }
}
