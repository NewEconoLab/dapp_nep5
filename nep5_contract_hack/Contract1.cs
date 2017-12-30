using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using System;
using System.ComponentModel;
using System.Numerics;

namespace Nep5_Contract
{
    //this is a shadow nep5 contract,which is implemented by nep4.
    //这是一个nep5影子合约，他的地址不需要改变，使用NEP4实现。
    //超级管理员可以用__SetCall方法改变他所指向的Nep5合约
    public class ContractNep5 : SmartContract
    {
        public static readonly byte[] SuperAdmin = Helper.ToScriptHash("ALjSnMZidJqd18iQaoCgFun6iqWRm2cVtj");
        public delegate byte[] Nep5Call(string method, object[] args);
        //nep5 func

        public static object Main(string method, object[] args)
        {
            if (method == "__SetCall")
            {
                if (!Runtime.CheckWitness(SuperAdmin)) return false;
                Storage.Put(Storage.CurrentContext, "target", (byte[])args[0]);
                return true;
            }
            var targetaddr = Storage.Get(Storage.CurrentContext, "target");

            //this is a nep4 call
            var dyncall = (Nep5Call)Helper.ToDelegate(targetaddr);
            var ret = dyncall(method, args);
            return ret;
        }

    }
}
