# Third-party notices

## Vortice.Vulkan

The files under `Bindings/Vulkan` are minimal handwritten Vulkan bindings following the
[Vortice.Vulkan](https://github.com/amerkoleci/Vortice.Vulkan) (commit `b5f06ab`, version 3.3.0)
API style and `Vulkan` namespace conventions, scoped to the subset used by `Alco.Graphics`.

Original license (MIT):

```
The MIT License (MIT)

Copyright (c) Amer Koleci and Contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## SharpMetal

The files under `Bindings/Metal` are minimal handwritten Metal/Objective-C bindings following
[SharpMetal](https://github.com/IsaacMarovitz/SharpMetal) (commit `107263f`)
API style and `Metal` namespace conventions, scoped to the subset used by `Alco.Graphics`.

Original license (MIT):

```
MIT License

Copyright (c) 2023 Isaac Marovitz

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## NoGraphicsAPI

The public API and the Vulkan and Metal backends of NoGraphicsSharp are a C# port of
[NoGraphicsAPI](https://github.com/sebbbi/NoGraphicsAPI) by Sebastian Aaltonen (MIT).

## Example windowing

The examples reference [Silk.NET.GLFW](https://github.com/dotnet/Silk.NET) 2.23.0 (MIT) through NuGet.
Its native dependency, [GLFW](https://www.glfw.org/) 3.4.0, uses the
[zlib/libpng license](https://github.com/glfw/glfw/blob/3.4/LICENSE.md).
These packages are not dependencies of the library projects.

## Example UI

The shared examples reference [ImGui.NET](https://github.com/ImGuiNET/ImGui.NET) 1.91.6.1 (MIT) through NuGet,
including native [cimgui](https://github.com/cimgui/cimgui) and [Dear ImGui](https://github.com/ocornut/imgui) (MIT) binaries.
`UIBatching` uses [SharpAstro.Fonts](https://github.com/SharpAstro/Fonts.Lib) 2.0.1171 (MIT) and its managed SharpAstro dependencies (Unlicense).
Its bundled `UIBatchingFont.ttf` is a renamed static subset of Noto Sans SC. The attribution and SIL OFL 1.1 license are in
[`examples/UIBatching/UIBatchingFont-OFL.txt`](examples/UIBatching/UIBatchingFont-OFL.txt).
These packages are not dependencies of the library projects.

## TextureCompression example assets

The PNGs under `examples/TextureCompression/Content/Textures` were supplied from the local Game project for this example.
Items and building atlases are copies of its artwork; complete AKM and SP277 firearm textures were exported through its runtime weapon compositor.
Source identities and alpha conventions are recorded in the example. No separate asset license was supplied; these images are not covered by
NoGraphicsSharp's MIT source-code license.

## Slang

The Slang compiler binaries under `src/NoGraphicsSharp.Slang/runtimes/<rid>/native` are patched builds of
[Slang](https://github.com/shader-slang/slang) 2026.22 from the [NoGraphicsSharp fork](https://github.com/IssacZhuang/slang).
The pinned source revision is recorded in the README. Slang is licensed under the Apache License, Version 2.0:
<https://www.apache.org/licenses/LICENSE-2.0>.
Windows ships `slang.dll` and `slang-compiler.dll`; Unix ships the fork's monolithic compiler library renamed to
`libslang.so` or `libslang.dylib` for the `slang` P/Invoke load name.

## Microsoft DirectX Shader Compiler

The unmodified `dxcompiler.dll` and `dxil.dll` in the Windows Slang runtime directories are from
[Microsoft DirectX Shader Compiler v1.9.2609](https://github.com/microsoft/DirectXShaderCompiler/releases/tag/v1.9.2609),
file version 1.9.2609.5 (`01b62ad47`), for x64 and ARM64. The official `dxc_2026_09_29.zip` archive has SHA-256
`ad31b1fc8443175d204f77a611fdb3ef2ec42759bdc2f1167368de24a4a7e7f1`.
The compiler and LLVM components carry the notices below; the Microsoft validator/signing component also carries
the Microsoft software license terms. These native binaries are not covered solely by this repository's MIT license.

### Microsoft MIT license

```text
Copyright (c) Microsoft Corporation.

MIT License

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED *AS IS*, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### Microsoft software license terms

```text
MICROSOFT SOFTWARE LICENSE TERMS

MICROSOFT DIRECTX SHADER COMPILER

These license terms are an agreement between you and Microsoft
Corporation (or one of its affiliates). They apply to the software named
above and any Microsoft services or software updates (except to the
extent such services or updates are accompanied by new or additional
terms, in which case those different terms apply prospectively and do
not alter your or Microsoft’s rights relating to pre-updated software or
services). IF YOU COMPLY WITH THESE LICENSE TERMS, YOU HAVE THE RIGHTS
BELOW. BY USING THE SOFTWARE, YOU ACCEPT THESE TERMS.

INSTALLATION AND USE RIGHTS.

General. Subject to the terms of this agreement, you may install and use any number of copies of the software, and solely for use on Windows.

Included Microsoft Applications. The software may include other Microsoft applications. These license terms apply to those included applications, if any, unless other license terms are provided with the other Microsoft applications.

Microsoft Platforms. The software may include components from Microsoft Windows. These components are governed by separate agreements and their own product support policies, as described in the license terms found in the installation directory for that component or in the “Licenses” folder accompanying the software.

Third Party Components. The software may include third party components with separate legal notices or governed by other agreements, as may be described in the ThirdPartyNotices file(s) accompanying the software.

DATA.

Data Collection. The software may collect information about you and your use of the software, and send that to Microsoft. Microsoft may use this information to provide services and improve our products and services. You may opt-out of many of these scenarios, but not all, as described in the product documentation.  There are also some features in the software that may enable you to collect data from users of your applications. If you use these features to enable data collection in your applications, you must comply with applicable law, including providing appropriate notices to users of your applications. You can learn more about data collection and use in the help documentation and the privacy statement at https://aka.ms/privacy. Your use of the software operates as your consent to these practices.

Processing of Personal Data. To the extent Microsoft is a processor or subprocessor of personal data in connection with the software, Microsoft makes the commitments in the European Union General Data Protection Regulation Terms of the Online Services Terms to all customers effective May 25, 2018, at https://docs.microsoft.com/en-us/legal/gdpr.

DISTRIBUTABLE CODE. The software may contain code you are permitted to distribute (i.e. make available for third parties) in applications you develop, as described in this Section.

Distribution Rights. The code and test files described below are distributable if included with the software.

Distributables. You may copy and distribute the object code form of the software listed in the distributables file list in the software; and

Third Party Distribution. You may permit distributors of your applications to copy and distribute any of this distributable code you elect to distribute with your applications.

Distribution Requirements. For any code you distribute, you must:

add significant primary functionality to it in your applications;

i.  require distributors and external end users to agree to terms that protect it and Microsoft at least as much as this agreement; and

ii. indemnify, defend, and hold harmless Microsoft from any claims, including attorneys’ fees, related to the distribution or use of your applications, except to the extent that any claim is based solely on the unmodified distributable code.

Distribution Restrictions. You may not:

use Microsoft’s trademarks or trade dress in your application in any way that suggests your application comes from or is endorsed by Microsoft; or modify or distribute the source code of any distributable code so that any part of it becomes subject to any license that requires that the distributable code, any other part of the software, or any of Microsoft’s other intellectual property be disclosed or distributed in source code form, or that others have the right to modify it.

SCOPE OF LICENSE. The software is licensed, not sold. Microsoft reserves all other rights. Unless applicable law gives you more rights despite this limitation, you will not (and have no right to):

work around any technical limitations in the software that only allow you to use it in certain ways;

reverse engineer, decompile or disassemble the software, or otherwise attempt to derive the source code for the software, except and to the extent required by third party licensing terms governing use of certain open source components that may be included in the software;

remove, minimize, block, or modify any notices of Microsoft or its suppliers in the software;

use the software in any way that is against the law or to create or propagate malware; or

share, publish, distribute, or lease the software (except for any distributable code, subject to the terms above), provide the software as a stand-alone offering for others to use, or transfer the software or this agreement to any third party.

EXPORT RESTRICTIONS. You must comply with all domestic and international export laws and regulations that apply to the software, which include restrictions on destinations, end users, and end use. For further information on export restrictions, visit https://aka.ms/exporting.

SUPPORT SERVICES. Microsoft is not obligated under this agreement to provide any support services for the software. Any support provided is “as is”, “with all faults”, and without warranty of any kind.

UPDATES. The software may periodically check for updates, and download and install them for you. You may obtain updates only from Microsoft or authorized sources. Microsoft may need to update your system to provide you with updates. You agree to receive these automatic updates without any additional notice. Updates may not include or support all existing software features, services, or peripheral devices.

ENTIRE AGREEMENT. This agreement, and any other terms Microsoft may provide for supplements, updates, or third-party applications, is the entire agreement for the software.

APPLICABLE LAW AND PLACE TO RESOLVE DISPUTES. If you acquired the software in the United States or Canada, the laws of the state or province where you live (or, if a business, where your principal place of business is located) govern the interpretation of this agreement, claims for its breach, and all other claims (including consumer protection, unfair competition, and tort claims), regardless of conflict of laws principles. If you acquired the software in any other country, its laws apply. If U.S. federal jurisdiction exists, you and Microsoft consent to exclusive jurisdiction and venue in the federal court in King County, Washington for all disputes heard in court. If not, you and Microsoft consent to exclusive jurisdiction and venue in the Superior Court of King County, Washington for all disputes heard in court.

CONSUMER RIGHTS; REGIONAL VARIATIONS. This agreement describes certain legal rights. You may have other rights, including consumer rights, under the laws of your state or country. Separate and apart from your relationship with Microsoft, you may also have rights with respect to the party from which you acquired the software. This agreement does not change those other rights if the laws of your state or country do not permit it to do so. For example, if you acquired the software in one of the below regions, or mandatory country law applies, then the following provisions apply to you:

a.  Australia. You have statutory guarantees under the Australian
    Consumer Law and nothing in this agreement is intended to affect
    those rights.

b.  Canada. If you acquired this software in Canada, you may stop
    receiving updates by turning off the automatic update feature,
    disconnecting your device from the Internet (if and when you
    re-connect to the Internet, however, the software will resume
    checking for and installing updates), or uninstalling the software.
    The product documentation, if any, may also specify how to turn off
    updates for your specific device or software.

c.  Germany and Austria.

  i. Warranty. The properly licensed software will perform substantially
  as described in any Microsoft materials that accompany the software.
  However, Microsoft gives no contractual guarantee in relation to the
  licensed software.

  ii. Limitation of Liability. In case of intentional conduct, gross
  negligence, claims based on the Product Liability Act, as well as, in
  case of death or personal or physical injury, Microsoft is liable
  according to the statutory law.

Subject to the foregoing clause ii., Microsoft will only be liable for slight negligence if Microsoft is in breach of such material contractual obligations, the fulfillment of which facilitate the due performance of this agreement, the breach of which would endanger the purpose of this agreement and the compliance with which a party may constantly trust in (so-called "cardinal obligations"). In other cases of slight negligence, Microsoft will not be liable for slight negligence.

DISCLAIMER OF WARRANTY. THE SOFTWARE IS LICENSED “AS IS.” YOU BEAR THE RISK OF USING IT. MICROSOFT GIVES NO EXPRESS WARRANTIES, GUARANTEES, OR CONDITIONS. TO THE EXTENT PERMITTED UNDER APPLICABLE LAWS, MICROSOFT EXCLUDES ALL IMPLIED WARRANTIES, INCLUDING MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE, AND NON-INFRINGEMENT.

LIMITATION ON AND EXCLUSION OF DAMAGES. IF YOU HAVE ANY BASIS FOR RECOVERING DAMAGES DESPITE THE PRECEDING DISCLAIMER OF WARRANTY, YOU CAN RECOVER FROM MICROSOFT AND ITS SUPPLIERS ONLY DIRECT DAMAGES UP TO U.S. $5.00. YOU CANNOT RECOVER ANY OTHER DAMAGES, INCLUDING CONSEQUENTIAL, LOST PROFITS, SPECIAL, INDIRECT, OR INCIDENTAL DAMAGES.

This limitation applies to (a) anything related to the software,
services, content (including code) on third party Internet sites, or
third party applications; and (b) claims for breach of contract,
warranty, guarantee, or condition; strict liability, negligence, or
other tort; or any other claim; in each case to the extent permitted by
applicable law.

It also applies even if Microsoft knew or should have known about the
possibility of the damages. The above limitation or exclusion may not
apply to you because your state, province, or country may not allow the
exclusion or limitation of incidental, consequential, or other damages.

Please note: As this software is distributed in Canada, some of the
clauses in this agreement are provided below in French.

Remarque: Ce logiciel étant distribué au Canada, certaines des clauses
dans ce contrat sont fournies ci-dessous en français.

EXONÉRATION DE GARANTIE. Le logiciel visé par une licence est offert «
tel quel ». Toute utilisation de ce logiciel est à votre seule risque et
péril. Microsoft n’accorde aucune autre garantie expresse. Vous pouvez
bénéficier de droits additionnels en vertu du droit local sur la
protection des consommateurs, que ce contrat ne peut modifier. La ou
elles sont permises par le droit locale, les garanties implicites de
qualité marchande, d’adéquation à un usage particulier et d’absence de
contrefaçon sont exclues.

LIMITATION DES DOMMAGES-INTÉRÊTS ET EXCLUSION DE RESPONSABILITÉ POUR LES
DOMMAGES. Vous pouvez obtenir de Microsoft et de ses fournisseurs une
indemnisation en cas de dommages directs uniquement à hauteur de 5,00 $
US. Vous ne pouvez prétendre à aucune indemnisation pour les autres
dommages, y compris les dommages spéciaux, indirects ou accessoires et
pertes de bénéfices.

Cette limitation concerne:

• tout ce qui est relié au logiciel, aux services ou au contenu (y
compris le code) figurant sur des sites Internet tiers ou dans des
programmes tiers; et

• les réclamations au titre de violation de contrat ou de garantie, ou
au titre de responsabilité stricte, de négligence ou d’une autre faute
dans la limite autorisée par la loi en vigueur.

Elle s’applique également, même si Microsoft connaissait ou devrait
connaître l’éventualité d’un tel dommage. Si votre pays n’autorise pas
l’exclusion ou la limitation de responsabilité pour les dommages
indirects, accessoires ou de quelque nature que ce soit, il se peut que
la limitation ou l’exclusion ci-dessus ne s’appliquera pas à votre
égard.

EFFET JURIDIQUE. Le présent contrat décrit certains droits juridiques.
Vous pourriez avoir d’autres droits prévus par les lois de votre pays.
Le présent contrat ne modifie pas les droits que vous confèrent les lois
de votre pays si celles-ci ne le permettent pas.
```

### Upstream third-party notices, including LLVM

```text
Microsoft/DirectXShaderCompiler

THIRD-PARTY SOFTWARE NOTICES AND INFORMATION

Do Not Translate or Localize

This project incorporates components from the projects listed below. The
original copyright notices and the licenses under which Microsoft received
such components are set forth below. Microsoft reserves all rights not
expressly granted herein, whether by implication, estoppel or otherwise.


* LLVM

==============================================================================
LLVM Release License
==============================================================================
University of Illinois/NCSA
Open Source License

Copyright (c) 2003-2015 University of Illinois at Urbana-Champaign.
All rights reserved.

Developed by:

    LLVM Team

    University of Illinois at Urbana-Champaign

    http://llvm.org

Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal with
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
of the Software, and to permit persons to whom the Software is furnished to do
so, subject to the following conditions:

    * Redistributions of source code must retain the above copyright notice,
      this list of conditions and the following disclaimers.

    * Redistributions in binary form must reproduce the above copyright notice,
      this list of conditions and the following disclaimers in the
      documentation and/or other materials provided with the distribution.

    * Neither the names of the LLVM Team, University of Illinois at
      Urbana-Champaign, nor the names of its contributors may be used to
      endorse or promote products derived from this Software without specific
      prior written permission.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.  IN NO EVENT SHALL THE
CONTRIBUTORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS WITH THE
SOFTWARE.

==============================================================================
Copyrights and Licenses for Third Party Software Distributed with LLVM:
==============================================================================
The LLVM software contains code written by third parties.  Such software will
have its own individual LICENSE.TXT file in the directory in which it appears.
This file will describe the copyrights, license, and restrictions which apply
to that code.

The disclaimer of warranty in the University of Illinois Open Source License
applies to all code in the LLVM Distribution, and nothing in any of the
other licenses gives permission to use the names of the LLVM Team or the
University of Illinois to endorse or promote products derived from this
Software.

The following pieces of software have additional or alternate copyrights,
licenses, and/or restrictions:

Program             Directory
-------             ---------
OpenBSD regex       llvm/lib/Support/{reg*, COPYRIGHT.regex}
pyyaml tests        llvm/test/YAMLParser/{*.data, LICENSE.TXT}
md5 contributions   llvm/lib/Support/MD5.cpp llvm/include/llvm/Support/MD5.h


* tools\clang

==============================================================================
LLVM Release License
==============================================================================
University of Illinois/NCSA
Open Source License

Copyright (c) 2007-2015 University of Illinois at Urbana-Champaign.
All rights reserved.

Developed by:

    LLVM Team

    University of Illinois at Urbana-Champaign

    http://llvm.org

Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal with
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
of the Software, and to permit persons to whom the Software is furnished to do
so, subject to the following conditions:

    * Redistributions of source code must retain the above copyright notice,
      this list of conditions and the following disclaimers.

    * Redistributions in binary form must reproduce the above copyright notice,
      this list of conditions and the following disclaimers in the
      documentation and/or other materials provided with the distribution.

    * Neither the names of the LLVM Team, University of Illinois at
      Urbana-Champaign, nor the names of its contributors may be used to
      endorse or promote products derived from this Software without specific
      prior written permission.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.  IN NO EVENT SHALL THE
CONTRIBUTORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS WITH THE
SOFTWARE.

==============================================================================
The LLVM software contains code written by third parties.  Such software will
have its own individual LICENSE.TXT file in the directory in which it appears.
This file will describe the copyrights, license, and restrictions which apply
to that code.

The disclaimer of warranty in the University of Illinois Open Source License
applies to all code in the LLVM Distribution, and nothing in any of the
other licenses gives permission to use the names of the LLVM Team or the
University of Illinois to endorse or promote products derived from this
Software.


* test\YAMLParser

Copyright (c) 2006 Kirill Simonov

Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
of the Software, and to permit persons to whom the Software is furnished to do
so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

* include\llvm\Support

LLVM System Interface Library
-------------------------------------------------------------------------------
The LLVM System Interface Library is licensed under the Illinois Open Source
License and has the following additional copyright:

Copyright (C) 2004 eXtensible Systems, Inc.

* OpenBSD regex

$OpenBSD: COPYRIGHT,v 1.3 2003/06/02 20:18:36 millert Exp $

Copyright 1992, 1993, 1994 Henry Spencer.  All rights reserved.
This software is not subject to any license of the American Telephone
and Telegraph Company or of the Regents of the University of California.

Permission is granted to anyone to use this software for any purpose on
any computer system, and to alter it and redistribute it, subject
to the following restrictions:

1. The author is not responsible for the consequences of use of this
   software, no matter how awful, even if they arise from flaws in it.

2. The origin of this software must not be misrepresented, either by
   explicit claim or by omission.  Since few users ever read sources,
   credits must appear in the documentation.

3. Altered versions must be plainly marked as such, and must not be
   misrepresented as being the original software.  Since few users
   ever read sources, credits must appear in the documentation.

4. This notice may not be removed or altered.

=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
/*-
 * Copyright (c) 1994
 *	The Regents of the University of California.  All rights reserved.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions
 * are met:
 * 1. Redistributions of source code must retain the above copyright
 *    notice, this list of conditions and the following disclaimer.
 * 2. Redistributions in binary form must reproduce the above copyright
 *    notice, this list of conditions and the following disclaimer in the
 *    documentation and/or other materials provided with the distribution.
 * 3. Neither the name of the University nor the names of its contributors
 *    may be used to endorse or promote products derived from this software
 *    without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE REGENTS AND CONTRIBUTORS ``AS IS'' AND
 * ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
 * IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
 * ARE DISCLAIMED.  IN NO EVENT SHALL THE REGENTS OR CONTRIBUTORS BE LIABLE
 * FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
 * DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS
 * OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION)
 * HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT
 * LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY
 * OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF
 * SUCH DAMAGE.
 *
 *	@(#)COPYRIGHT	8.1 (Berkeley) 3/16/94
 */

* lib\Headers Files

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
```
